using Microsoft.EntityFrameworkCore;
using WK7Bot.Core.Entities;
using WK7Bot.Core.Interfaces;

namespace WK7Bot.Infrastructure.Data;

/// <summary>
/// Entity Framework Core implementation of the waste dispatch state data access contract.
/// </summary>
public class WasteDispatchRepository : IWasteDispatchRepository
{
    private readonly BotDbContext _dbContext;

    /// <summary>
    /// Initializes a new instance of the <see cref="WasteDispatchRepository"/> class.
    /// </summary>
    /// <param name="dbContext">The database context instance.</param>
    public WasteDispatchRepository(BotDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <summary>
    /// Determines whether a dispatch of the given kind was already sent to a guild on a specific date.
    /// </summary>
    /// <param name="kind">The dispatch kind (see <c>WasteDispatchKinds</c>).</param>
    /// <param name="guildId">The target Discord guild ID.</param>
    /// <param name="sentOn">The calendar date the message would be sent on.</param>
    /// <param name="cancellationToken">Cancellation token to observe.</param>
    /// <returns><see langword="true"/> when the message was already sent; otherwise <see langword="false"/>.</returns>
    public async Task<bool> HasSentAsync(string kind, ulong guildId, DateTime sentOn, CancellationToken cancellationToken = default)
    {
        var normalizedDate = sentOn.Date;

        return await _dbContext.WasteDispatchLogs
            .AsNoTracking()
            .AnyAsync(
                log => log.Kind == kind && log.GuildId == guildId && log.SentOn == normalizedDate,
                cancellationToken);
    }

    /// <summary>
    /// Records that a dispatch of the given kind was sent to a guild on a specific date. Idempotent.
    /// </summary>
    /// <param name="kind">The dispatch kind (see <c>WasteDispatchKinds</c>).</param>
    /// <param name="guildId">The target Discord guild ID.</param>
    /// <param name="sentOn">The calendar date the message was sent on.</param>
    /// <param name="cancellationToken">Cancellation token to observe.</param>
    /// <returns>A task representing the asynchronous storage operation.</returns>
    public async Task MarkSentAsync(string kind, ulong guildId, DateTime sentOn, CancellationToken cancellationToken = default)
    {
        var normalizedDate = sentOn.Date;

        var alreadySent = await _dbContext.WasteDispatchLogs
            .AnyAsync(
                log => log.Kind == kind && log.GuildId == guildId && log.SentOn == normalizedDate,
                cancellationToken);

        if (alreadySent)
        {
            return;
        }

        var log = new WasteDispatchLog { Kind = kind, GuildId = guildId, SentOn = normalizedDate };
        await _dbContext.WasteDispatchLogs.AddAsync(log, cancellationToken);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (DatabaseWriteGuard.IsUniqueConstraintViolation(ex))
        {
            // Lost the race against a concurrent dispatcher — the row already exists, which is the
            // desired state. Detach the failed insert so the shared context stays usable.
            _dbContext.Entry(log).State = EntityState.Detached;
        }
    }

    /// <summary>
    /// Removes dispatch records older than the given retention window so the table stays bounded
    /// (it also stores the <c>weekend_digest</c> dispatch records).
    /// </summary>
    /// <param name="olderThan">Records strictly older than this date are removed.</param>
    /// <param name="cancellationToken">Cancellation token to observe.</param>
    /// <returns>The number of records removed.</returns>
    public async Task<int> PruneAsync(DateTime olderThan, CancellationToken cancellationToken = default)
    {
        var cutoff = olderThan.Date;

        var stale = await _dbContext.WasteDispatchLogs
            .Where(log => log.SentOn < cutoff)
            .ToListAsync(cancellationToken);

        if (stale.Count == 0)
        {
            return 0;
        }

        _dbContext.WasteDispatchLogs.RemoveRange(stale);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return stale.Count;
    }
}
