namespace WK7Bot.Services.Interfaces;

/// <summary>
/// Defines client operations for interacting with the Home Assistant REST API.
/// </summary>
public interface IHomeAssistantService
{
    /// <summary>
    /// Executes an HTTP GET request against the Home Assistant REST API via the Supervisor proxy connection.
    /// </summary>
    /// <param name="endpoint">The relative endpoint path to request from the REST API.</param>
    /// <param name="cancellationToken">Cancellation token to cancel the request execution.</param>
    /// <returns>The raw string payload returned by the Home Assistant API.</returns>
    Task<string?> GetApiStateAsync(string endpoint, CancellationToken cancellationToken = default);
}