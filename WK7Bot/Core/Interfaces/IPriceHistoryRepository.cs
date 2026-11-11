namespace WK7Bot.Core.Interfaces;

using WK7Bot.Core.Entities;

/// <summary>
/// Repository contract for persisting and querying price history snapshots.
/// </summary>
public interface IPriceHistoryRepository
{
    /// <summary>
    /// Records a collection of price snapshots, skipping any duplicates for the same app/store/time bucket.
    /// </summary>
    /// <param name="snapshots">The snapshots to persist.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The number of new snapshots stored.</returns>
    Task<int> AddSnapshotsAsync(IEnumerable<PriceHistorySnapshot> snapshots, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves the most recent price snapshots for an AppID, optionally filtered by store.
    /// </summary>
    /// <param name="appId">The Steam application identifier.</param>
    /// <param name="since">Only return snapshots recorded at or after this UTC time.</param>
    /// <param name="store">Optional store filter.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The matching snapshots, ordered from oldest to newest.</returns>
    Task<IReadOnlyList<PriceHistorySnapshot>> GetSnapshotsAsync(uint appId, DateTime since, string? store = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes snapshots older than the specified retention period.
    /// </summary>
    /// <param name="olderThan">The cutoff UTC time.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The number of removed snapshots.</returns>
    Task<int> PruneAsync(DateTime olderThan, CancellationToken cancellationToken = default);
}
