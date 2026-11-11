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

    /// <summary>Updates the last observed price, discount and alert timestamp for multiple entries in a single round-trip.</summary>
    Task UpdatePricesAsync(IEnumerable<DealWatchPriceUpdate> updates, CancellationToken cancellationToken = default);
}

/// <summary>
/// A single price update for a deal watchlist entry.
/// </summary>
/// <param name="Id">The deal watchlist entry identifier.</param>
/// <param name="LastPrice">The last observed price.</param>
/// <param name="LastDiscount">The last observed discount percentage.</param>
/// <param name="LastAlertedAt">The UTC timestamp of the last alert, if any.</param>
public readonly record struct DealWatchPriceUpdate(int Id, decimal? LastPrice, int LastDiscount, DateTime? LastAlertedAt);
