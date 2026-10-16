namespace WK7Bot.Core.Interfaces;

using WK7Bot.Core.Entities;

/// <summary>
/// Repository contract for per-user deal watchlist entries.
/// </summary>
public interface IDealWatchRepository
{
    /// <summary>Returns every watchlist entry.</summary>
    Task<IReadOnlyList<DealWatchItem>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns the watchlist entries for one Discord user.</summary>
    Task<IReadOnlyList<DealWatchItem>> GetForUserAsync(ulong discordUserId, CancellationToken cancellationToken = default);

    /// <summary>Adds a new watchlist entry.</summary>
    Task AddAsync(DealWatchItem item, CancellationToken cancellationToken = default);

    /// <summary>Removes a watchlist entry by user and game name.</summary>
    Task<bool> RemoveAsync(ulong discordUserId, string gameName, CancellationToken cancellationToken = default);

    /// <summary>Updates the last observed price, discount and alert timestamp for an entry.</summary>
    Task UpdatePriceAsync(int id, decimal? lastPrice, int lastDiscount, DateTime? lastAlertedAt, CancellationToken cancellationToken = default);
}
