namespace WK7Bot.Infrastructure.Data;

using Microsoft.EntityFrameworkCore;
using WK7Bot.Core.Entities;
using WK7Bot.Core.Interfaces;

/// <summary>
/// SQLite-backed repository for wishlist game tracking and alert cooldown management.
/// </summary>
public class WishlistDealRepository : IWishlistDealRepository
{
    private readonly BotDbContext _context;

    /// <summary>
    /// Initializes a new instance of the <see cref="WishlistDealRepository"/> class.
    /// </summary>
    /// <param name="context">The database context instance.</param>
    public WishlistDealRepository(BotDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<WishlistGame>> GetAllTrackedGamesAsync(CancellationToken cancellationToken = default)
    {
        return await _context.WishlistGames
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task ReplaceTrackedGamesAsync(IEnumerable<WishlistGame> games, CancellationToken cancellationToken = default)
    {
        await _context.WishlistGames.ExecuteDeleteAsync(cancellationToken);
        await _context.WishlistGames.AddRangeAsync(games, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> IsOnCooldownAsync(uint appId, TimeSpan cooldown, CancellationToken cancellationToken = default)
    {
        var cutoff = DateTimeOffset.UtcNow - cooldown;
        return await _context.WishlistAlertHistories
            .AsNoTracking()
            .AnyAsync(h => h.AppId == appId && h.AlertedAt >= cutoff, cancellationToken);
    }

    /// <inheritdoc />
    public async Task RecordAlertAsync(uint appId, bool isHistoricalLow, CancellationToken cancellationToken = default)
    {
        _context.WishlistAlertHistories.Add(new WishlistAlertHistory
        {
            AppId = appId,
            AlertedAt = DateTimeOffset.UtcNow,
            WasHistoricalLow = isHistoricalLow
        });
        await _context.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task PruneAlertHistoryAsync(TimeSpan retention, CancellationToken cancellationToken = default)
    {
        var cutoff = DateTimeOffset.UtcNow - retention;
        await _context.WishlistAlertHistories
            .Where(h => h.AlertedAt < cutoff)
            .ExecuteDeleteAsync(cancellationToken);
    }
}
