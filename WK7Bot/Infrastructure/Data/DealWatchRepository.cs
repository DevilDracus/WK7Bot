namespace WK7Bot.Infrastructure.Data;

using Microsoft.EntityFrameworkCore;
using WK7Bot.Core.Entities;
using WK7Bot.Core.Interfaces;

/// <summary>
/// SQLite-backed repository for per-user deal watchlist entries.
/// </summary>
public class DealWatchRepository : IDealWatchRepository
{
    private readonly IDbContextFactory<BotDbContext> _dbContextFactory;

    /// <summary>Initializes a new instance of the <see cref="DealWatchRepository"/> class.</summary>
    /// <param name="dbContextFactory">The database context factory.</param>
    public DealWatchRepository(IDbContextFactory<BotDbContext> dbContextFactory)
    {
        _dbContextFactory = dbContextFactory ?? throw new ArgumentNullException(nameof(dbContextFactory));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DealWatchItem>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await context.DealWatchItems.AsNoTracking().ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DealWatchItem>> GetForUserAsync(ulong discordUserId, CancellationToken cancellationToken = default)
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await context.DealWatchItems
            .AsNoTracking()
            .Where(w => w.DiscordUserId == discordUserId)
            .OrderBy(w => w.GameName)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task AddAsync(DealWatchItem item, CancellationToken cancellationToken = default)
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        context.DealWatchItems.Add(item);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<bool> RemoveAsync(ulong discordUserId, string gameName, CancellationToken cancellationToken = default)
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var existing = await context.DealWatchItems
            .FirstOrDefaultAsync(w => w.DiscordUserId == discordUserId && w.GameName == gameName, cancellationToken);
        if (existing is null)
        {
            return false;
        }

        context.DealWatchItems.Remove(existing);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <inheritdoc />
    public async Task UpdatePriceAsync(int id, decimal? lastPrice, int lastDiscount, DateTime? lastAlertedAt, CancellationToken cancellationToken = default)
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var existing = await context.DealWatchItems.FirstOrDefaultAsync(w => w.Id == id, cancellationToken);
        if (existing is null)
        {
            return;
        }

        existing.LastKnownPrice = lastPrice;
        existing.LastKnownDiscount = lastDiscount;
        existing.LastAlertedAt = lastAlertedAt;
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
