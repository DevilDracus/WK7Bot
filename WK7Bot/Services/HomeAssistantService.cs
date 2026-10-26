using System.Diagnostics;
using System.Net.Http.Headers;
using WK7Bot.Core.Utilities;
using WK7Bot.Services.Interfaces;

namespace WK7Bot.Services;

/// <summary>
/// Communicates with Home Assistant through the internal Supervisor proxy using Bearer token authentication.
/// </summary>
public class HomeAssistantService : IHomeAssistantService
{
    /// <summary>
    /// Base address of the Supervisor-proxied Home Assistant REST API.
    /// </summary>
    private static readonly Uri SupervisorApiBase = new("http://supervisor/core/api/");

    private readonly HttpClient _httpClient;
    private readonly ILogger<HomeAssistantService> _logger;
    private readonly MetricsRegistry _metrics;
    private readonly string? _supervisorToken;

    /// <summary>
    /// Initializes a new instance of the <see cref="HomeAssistantService"/> class.
    /// </summary>
    /// <param name="httpClient">The HTTP client instance configured for REST communication.</param>
    /// <param name="logger">The diagnostic logging service.</param>
    /// <param name="metrics">The metrics registry.</param>
    /// <exception cref="ArgumentNullException">Thrown when a required dependency is null.</exception>
    public HomeAssistantService(HttpClient httpClient, ILogger<HomeAssistantService> logger, MetricsRegistry metrics)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));

        _supervisorToken = Environment.GetEnvironmentVariable("SUPERVISOR_TOKEN");
        if (string.IsNullOrEmpty(_supervisorToken))
        {
            _logger.LogWarning("SUPERVISOR_TOKEN environment variable is missing. Home Assistant API requests will be unauthenticated.");
        }
    }

    /// <summary>
    /// Executes an HTTP GET request against the Home Assistant REST API via the Supervisor proxy connection.
    /// </summary>
    /// <param name="endpoint">The relative endpoint path to request from the REST API.</param>
    /// <param name="cancellationToken">Cancellation token to cancel the request execution.</param>
    /// <returns>The raw string payload returned by the Home Assistant API, or null when the call failed.</returns>
    public async Task<string?> GetApiStateAsync(string endpoint, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var response = await HttpRetry.ExecuteAsync(
                async () =>
                {
                    var req = new HttpRequestMessage(HttpMethod.Get, BuildRequestUri(endpoint));
                    if (!string.IsNullOrEmpty(_supervisorToken))
                    {
                        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _supervisorToken);
                    }
                    return await _httpClient.SendAsync(req, cancellationToken);
                },
                3,
                r => (int)r.StatusCode >= 500 || r.StatusCode == System.Net.HttpStatusCode.TooManyRequests,
                attempt => TimeSpan.FromMilliseconds(500 * (1 << (attempt - 1))),
                null,
                null,
                cancellationToken);

            response.EnsureSuccessStatusCode();
            var payload = await response.Content.ReadAsStringAsync(cancellationToken);

            stopwatch.Stop();
            _metrics.RecordHttp($"HomeAssistant:{endpoint}", stopwatch.Elapsed.TotalMilliseconds, true);
            return payload;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            stopwatch.Stop();
            _metrics.RecordHttp($"HomeAssistant:{endpoint}", stopwatch.Elapsed.TotalMilliseconds, false);

            // Shutdown cancellation is not a Home Assistant problem; everything else is reported.
            _logger.LogError(ex, "Failed to communicate with Home Assistant API endpoint: {Endpoint}", endpoint);
            return null;
        }
    }

    /// <summary>
    /// Resolves the endpoint relative to the Supervisor proxy base address, tolerating a leading slash.
    /// </summary>
    /// <param name="endpoint">The relative endpoint path.</param>
    /// <returns>The absolute request URI.</returns>
    private static Uri BuildRequestUri(string endpoint)
        => new(SupervisorApiBase, (endpoint ?? string.Empty).TrimStart('/'));
}
