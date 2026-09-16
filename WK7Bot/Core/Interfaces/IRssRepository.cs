namespace WK7Bot.Core.Interfaces;

using WK7Bot.Core.Entities;

/// <summary>
/// Defines data access operations for managing RSS feeds and dashboard configuration state.
/// </summary>
public interface IRssRepository
{
    /// <summary>
    /// Retrieves all active RSS feeds registered in the system.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token to observe.</param>
    /// <returns>A collection of all configured RSS feeds.</returns>
    Task<List<RssFeed>> GetAllFeedsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves a specific RSS feed by its display name.
    /// </summary>
    /// <param name="name">The name of the RSS feed.</param>
    /// <param name="cancellationToken">Cancellation token to observe.</param>
    /// <returns>The matching RSS feed entity if found; otherwise, null.</returns>
    Task<RssFeed?> GetFeedByNameAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a new RSS feed entity to the storage context.
    /// </summary>
    /// <param name="feed">The feed entity to insert.</param>
    /// <param name="cancellationToken">Cancellation token to observe.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task AddFeedAsync(RssFeed feed, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an existing RSS feed entity with new state data.
    /// </summary>
    /// <param name="feed">The modified feed entity.</param>
    /// <param name="cancellationToken">Cancellation token to observe.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task UpdateFeedAsync(RssFeed feed, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes an RSS feed entity by its unique identifier.
    /// </summary>
    /// <param name="id">The database identifier of the feed to remove.</param>
    /// <param name="cancellationToken">Cancellation token to observe.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task DeleteFeedAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores or updates the active Discord dashboard message tracking location.
    /// </summary>
    /// <param name="channelId">The channel ID where the dashboard is posted.</param>
    /// <param name="messageId">The message ID of the active dashboard component.</param>
    /// <param name="cancellationToken">Cancellation token to observe.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task SaveDashboardLocationAsync(ulong channelId, ulong messageId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves the saved dashboard message location if one has been posted.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token to observe.</param>
    /// <returns>A tuple containing the channel ID and message ID, or null if not set.</returns>
    Task<(ulong ChannelId, ulong MessageId)?> GetDashboardLocationAsync(CancellationToken cancellationToken = default);
}