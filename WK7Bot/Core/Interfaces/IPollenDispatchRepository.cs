namespace WK7Bot.Core.Interfaces;

using WK7Bot.Core.Entities;

/// <summary>
/// Defines data access operations for persisting the last pollen forecast communicated to a guild,
/// so the 3-day cadence and DWD revisions are evaluated consistently across restarts.
/// </summary>
public interface IPollenDispatchRepository
{
    /// <summary>
    /// Returns the last persisted pollen dispatch state for the given guild, or <see langword="null"/>
    /// when no post has been recorded yet.
    /// </summary>
    /// <param name="guildId">The target Discord guild ID.</param>
    /// <param name="cancellationToken">Cancellation token to observe.</param>
    /// <returns>The persisted state, or <see langword="null"/>.</returns>
    Task<PollenDispatchState?> GetLastAsync(ulong guildId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Inserts or updates the persisted pollen dispatch state for the given guild. Idempotent.
    /// </summary>
    /// <param name="guildId">The target Discord guild ID.</param>
    /// <param name="lastPostedDate">The calendar date the forecast was posted on.</param>
    /// <param name="forecastJson">The serialized forecast aligned to <paramref name="lastPostedDate"/>.</param>
    /// <param name="cancellationToken">Cancellation token to observe.</param>
    /// <returns>A task representing the asynchronous storage operation.</returns>
    Task UpsertAsync(ulong guildId, DateTime lastPostedDate, string forecastJson, CancellationToken cancellationToken = default);
}