namespace WK7Bot.Core.Interfaces;

using WK7Bot.Core.Entities;

/// <summary>
/// Repository contract for managing tracked wishlist games and alert cooldown history.
/// </summary>
public interface IWishlistDealRepository
{
    /// <summary>
    /// Retrieves all currently tracked wishlist games.
    /// </summary>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A collection of tracked wishlist games.</returns>
    Task<IReadOnlyList<WishlistGame>> GetAllTrackedGamesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces the entire set of tracked games with a new collection.
    /// </summary>
    /// <param name="games">The new collection of games to track.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task ReplaceTrackedGamesAsync(IEnumerable<WishlistGame> games, CancellationToken cancellationToken = default);

    /// <summary>
    /// Determines whether an alert has been posted for the specified game within the cooldown window.
    /// </summary>
    /// <param name="appId">The Steam application identifier.</param>
    /// <param name="cooldown">The cooldown duration to check against.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>True when an alert was posted within the cooldown window; otherwise false.</returns>
    Task<bool> IsOnCooldownAsync(uint appId, TimeSpan cooldown, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records that an alert was posted for the specified game.
    /// </summary>
    /// <param name="appId">The Steam application identifier.</param>
    /// <param name="isHistoricalLow">Whether the alert was for a historical low price.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task RecordAlertAsync(uint appId, bool isHistoricalLow, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes alert history entries older than the specified retention period.
    /// </summary>
    /// <param name="retention">The maximum age of entries to keep.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task PruneAlertHistoryAsync(TimeSpan retention, CancellationToken cancellationToken = default);
}
