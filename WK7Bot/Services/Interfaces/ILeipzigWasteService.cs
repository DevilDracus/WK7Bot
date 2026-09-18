namespace WK7Bot.Services.Interfaces;

/// <summary>
/// Service interface defining contract capabilities for parsing and fetching Leipzig waste collection schedules.
/// </summary>
public interface ILeipzigWasteService
{
    /// <summary>
    /// Downloads and parses the ICS feed to retrieve waste collection summaries for a target calendar date.
    /// </summary>
    /// <param name="targetDate">The target date to evaluate against calendar events.</param>
    /// <param name="cancellationToken">Cancellation token for network request execution.</param>
    /// <returns>A list of user-friendly waste collection descriptions scheduled for the given date.</returns>
    Task<List<string>> GetWasteTypesForDateAsync(DateTime targetDate, CancellationToken cancellationToken = default);
}