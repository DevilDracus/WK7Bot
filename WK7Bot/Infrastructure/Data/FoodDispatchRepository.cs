using Microsoft.EntityFrameworkCore;
using WK7Bot.Core.Entities;
using WK7Bot.Core.Interfaces;

namespace WK7Bot.Infrastructure.Data;

/// <summary>
/// Entity Framework Core implementation of the food dispatch state data access contract.
/// </summary>
public class FoodDispatchRepository : IFoodDispatchRepository
{
    private readonly IDbContextFactory<BotDbContext> _dbContextFactory;

    /// <summary>
    /// Initializes a new instance of the <see cref="FoodDispatchRepository" /> class.
    /// </summary>
    /// <param name="dbContextFactory">The database context factory.</param>
    public FoodDispatchRepository(IDbContextFactory<BotDbContext> dbContextFactory)
    {
        _dbContextFactory = dbContextFactory;
    }

    /// <summary>
    /// Determines whether a dispatch of the given kind was already sent on a specific date.
    /// </summary>
    /// <param name="kind">The dispatch kind (see <c>FoodDispatchKinds</c>).</param>
    /// <param name="sentOn">The calendar date the message would be sent on.</param>
    /// <param name="cancellationToken">Cancellation token to observe.</param>
    /// <returns><see langword="true" /> when the message was already sent; otherwise, <see langword="false" />.</returns>
    public async Task<bool> HasSentAsync(string kind, DateTime sentOn, CancellationToken cancellationToken = default)
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var normalizedDate = sentOn.Date;

        return await context.FoodDispatchLogs
            .AsNoTracking()
            .AnyAsync(
                log => log.Kind == kind && log.SentOn == normalizedDate,
                cancellationToken);
    }

    /// <summary>
    /// Records that a dispatch of the given kind was sent on a specific date. Idempotent.
    /// </summary>
    /// <param name="kind">The dispatch kind (see <c>FoodDispatchKinds</c>).</param>
    /// <param name="sentOn">The calendar date the message was sent on.</param>
    /// <param name="cancellationToken">Cancellation token to observe.</param>
    /// <returns>A task representing the asynchronous storage operation.</returns>
    public async Task MarkSentAsync(string kind, DateTime sentOn, CancellationToken cancellationToken = default)
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var normalizedDate = sentOn.Date;

        var alreadySent = await context.FoodDispatchLogs
            .AnyAsync(
                log => log.Kind == kind && log.SentOn == normalizedDate,
                cancellationToken);

        if (alreadySent)
        {
            return;
        }

        var log = new FoodDispatchLog { Kind = kind, SentOn = normalizedDate };
        await context.FoodDispatchLogs.AddAsync(log, cancellationToken);

        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (DatabaseWriteGuard.IsUniqueConstraintViolation(ex))
        {
            // Lost the race against a concurrent dispatcher — the row already exists, which is the
            // desired state. Detach the failed insert so the shared context stays usable.
            context.Entry(log).State = EntityState.Detached;
        }
    }

    /// <summary>
    /// Removes dispatch records older than the given retention window so the table stays bounded.
    /// </summary>
    /// <param name="olderThan">Records strictly older than this date are removed.</param>
    /// <param name="cancellationToken">Cancellation token to observe.</param>
    /// <returns>The number of records removed.</returns>
    public async Task<int> PruneAsync(DateTime olderThan, CancellationToken cancellationToken = default)
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var cutoff = olderThan.Date;

        var stale = await context.FoodDispatchLogs
            .Where(log => log.SentOn < cutoff)
            .ToListAsync(cancellationToken);

        if (stale.Count == 0)
        {
            return 0;
        }

        context.FoodDispatchLogs.RemoveRange(stale);
        await context.SaveChangesAsync(cancellationToken);
        return stale.Count;
    }
}