namespace WK7Bot.Infrastructure.Data;

using Microsoft.EntityFrameworkCore;
using WK7Bot.Core.Entities;
using WK7Bot.Core.Interfaces;

/// <summary>
/// SQLite-backed repository for wishlist game tracking and alert cooldown management.
/// </summary>
public class WishlistDealRepository : IWishlistDealRepository
{
    private readonly IDbContextFactory<BotDbContext> _dbContextFactory;

    /// <summary>
    /// Initializes a new instance of the <see cref="WishlistDealRepository" /> class.
    /// </summary>
    /// <param name="dbContextFactory">The database context factory.</param>
    public WishlistDealRepository(IDbContextFactory<BotDbContext> dbContextFactory)
    {
        _dbContextFactory = dbContextFactory ?? throw new ArgumentNullException(nameof(dbContextFactory));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<WishlistGame>> GetAllTrackedGamesAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await context.WishlistGames
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task ReplaceTrackedGamesAsync(IEnumerable<WishlistGame> games, CancellationToken cancellationToken = default)
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var existing = await context.WishlistGames.ToListAsync(cancellationToken);
            context.WishlistGames.RemoveRange(existing);
            await context.WishlistGames.AddRangeAsync(games, cancellationToken).ConfigureAwait(false);
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception)
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<bool> IsOnCooldownAsync(uint appId, TimeSpan cooldown, CancellationToken cancellationToken = default)
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var cutoff = DateTimeOffset.UtcNow - cooldown;
        return await context.WishlistAlertHistories
            .AsNoTracking()
            .AnyAsync(h => h.AppId == appId && h.AlertedAt >= cutoff, cancellationToken);
    }

    /// <inheritdoc />
    public async Task RecordAlertAsync(uint appId, bool isHistoricalLow, CancellationToken cancellationToken = default)
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        context.WishlistAlertHistories.Add(new WishlistAlertHistory
        {
            AppId = appId,
            AlertedAt = DateTimeOffset.UtcNow,
            WasHistoricalLow = isHistoricalLow
        });
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task PruneAlertHistoryAsync(TimeSpan retention, CancellationToken cancellationToken = default)
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var cutoff = DateTimeOffset.UtcNow - retention;
        var stale = await context.WishlistAlertHistories
            .Where(h => h.AlertedAt < cutoff)
            .ToListAsync(cancellationToken);

        if (stale.Count > 0)
        {
            context.WishlistAlertHistories.RemoveRange(stale);
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}