using System.Net.Http.Headers;
using Microsoft.Extensions.Logging;

namespace WK7Bot.Services;

/// <summary>
/// Communicates with Home Assistant through the internal Supervisor proxy using Bearer token authentication.
/// </summary>
public class HomeAssistantService : IHomeAssistantService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<HomeAssistantService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="HomeAssistantService"/> class and configures default headers.
    /// </summary>
    /// <param name="httpClient">The HTTP client instance configured for REST communication.</param>
    /// <param name="logger">The diagnostic logging service.</param>
    public HomeAssistantService(HttpClient httpClient, ILogger<HomeAssistantService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;

        string? supervisorToken = Environment.GetEnvironmentVariable("SUPERVISOR_TOKEN");
        
        _httpClient.BaseAddress = new Uri("http://supervisor/core/api/");

        if (!string.IsNullOrEmpty(supervisorToken))
        {
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", supervisorToken);
        }
        else
        {
            _logger.LogWarning("SUPERVISOR_TOKEN environment variable is missing. Home Assistant API requests will be unauthenticated.");
        }
    }

    /// <summary>
    /// Executes an HTTP GET request against the Home Assistant REST API via the Supervisor proxy connection.
    /// </summary>
    /// <param name="endpoint">The relative endpoint path to request from the REST API.</param>
    /// <param name="cancellationToken">Cancellation token to cancel the request execution.</param>
    /// <returns>The raw string payload returned by the Home Assistant API.</returns>
    public async Task<string?> GetApiStateAsync(string endpoint, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.GetAsync(endpoint, cancellationToken);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to communicate with Home Assistant API endpoint: {Endpoint}", endpoint);
            return null;
        }
    }
}