namespace WK7Bot.Services.Interfaces;

using WK7Bot.Core.Entities;

/// <summary>
/// Manages per-user deal watchlists and evaluates price drops against the user's thresholds.
/// </summary>
public interface IDealWatchlistService
{
    /// <summary>Adds a game to the invoking user's watchlist.</summary>
    Task<DealWatchResult> WatchAsync(ulong discordUserId, string gameName, int? minDiscountPercent = null, decimal? targetPrice = null, CancellationToken cancellationToken = default);

    /// <summary>Removes a game from the invoking user's watchlist.</summary>
    Task<DealWatchResult> UnwatchAsync(ulong discordUserId, string gameName, CancellationToken cancellationToken = default);

    /// <summary>Returns the invoking user's watchlist.</summary>
    Task<IReadOnlyList<DealWatchItem>> GetWatchlistAsync(ulong discordUserId, CancellationToken cancellationToken = default);

    /// <summary>Checks every watchlist entry and sends DM alerts where thresholds are met.</summary>
    Task<int> CheckAllAsync(CancellationToken cancellationToken = default);
}

/// <summary>Result of a watchlist operation.</summary>
/// <param name="Success">Whether the operation succeeded.</param>
/// <param name="Message">A human-readable message.</param>
public sealed record DealWatchResult(bool Success, string Message);
