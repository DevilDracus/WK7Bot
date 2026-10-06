namespace WK7Bot.Services.Interfaces;

using WK7Bot.Models;

/// <summary>
/// Provides the public events happening over an upcoming weekend (Friday through Sunday),
/// used as the raw pool for the weekend digest.
/// </summary>
public interface IWeekendEventSource
{
    /// <summary>
    /// Fetches and merges all listings that overlap the weekend starting on the given Friday.
    /// </summary>
    /// <param name="weekendFriday">The Friday date identifying the target weekend.</param>
    /// <param name="cancellationToken">Cancellation token for network operations.</param>
    /// <returns>The deduplicated events of that weekend.</returns>
    Task<IReadOnlyList<WeekendEvent>> FetchWeekendEventsAsync(DateTime weekendFriday, CancellationToken cancellationToken = default);
}
