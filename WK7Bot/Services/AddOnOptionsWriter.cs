namespace WK7Bot.Services;

using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using WK7Bot.Core.Interfaces;
using WK7Bot.Core.Utilities;

/// <summary>
/// Best-effort writer for the Home Assistant add-on options. It merges a patch into the current
/// <c>/data/options.json</c> document and posts the result to the Supervisor self-options endpoint so
/// mappings created through <c>/link</c> survive an add-on restart and appear in the Home Assistant UI.
/// </summary>
public class AddOnOptionsWriter : IAddOnOptionsWriter
{
    private const string OptionsFilePath = "/data/options.json";

    private readonly HttpClient _httpClient;
    private readonly ILogger<AddOnOptionsWriter> _logger;
    private readonly string? _supervisorToken;

    /// <summary>Initializes a new instance of the <see cref="AddOnOptionsWriter"/> class.</summary>
    /// <param name="httpClient">The Supervisor HTTP client.</param>
    /// <param name="logger">The logger instance.</param>
    public AddOnOptionsWriter(HttpClient httpClient, ILogger<AddOnOptionsWriter> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _supervisorToken = Environment.GetEnvironmentVariable("SUPERVISOR_TOKEN");
    }

    /// <summary>
    /// Reads the current add-on options. Returns an empty object when the file is unavailable (for
    /// example during local development outside Home Assistant).
    /// </summary>
    /// <returns>The parsed options object.</returns>
    public async Task<JsonObject> ReadCurrentOptionsAsync()
    {
        try
        {
            var text = await File.ReadAllTextAsync(OptionsFilePath).ConfigureAwait(false);
            return JsonNode.Parse(text) as JsonObject ?? new JsonObject();
        }
        catch (FileNotFoundException)
        {
            return new JsonObject();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not read add-on options from {Path}.", OptionsFilePath);
            return new JsonObject();
        }
    }

    /// <summary>
    /// Merges the supplied top-level keys into the current options and writes the result back through
    /// the Supervisor API.
    /// </summary>
    /// <param name="patch">The top-level option keys to replace.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns><see langword="true"/> when the Supervisor accepted the update.</returns>
    public async Task<bool> MergeAsync(IDictionary<string, object?> patch, CancellationToken cancellationToken = default)
    {
        var options = await ReadCurrentOptionsAsync().ConfigureAwait(false);
        foreach (var (key, value) in patch)
        {
            options[key] = JsonSerializer.SerializeToNode(value);
        }

        return await WriteOptionsAsync(options, cancellationToken);
    }

    /// <summary>
    /// Merges a single <c>features.&lt;key&gt;</c> switch into the current options and writes the result
    /// through the Supervisor API. Used by the runtime <c>/config feature</c> command.
    /// </summary>
    /// <param name="featureKey">The snake_case feature key (e.g. <c>food_service_enabled</c>).</param>
    /// <param name="enabled">The new state.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns><see langword="true"/> when the Supervisor accepted the update.</returns>
    public async Task<bool> MergeFeatureAsync(string featureKey, bool enabled, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(featureKey))
        {
            return false;
        }

        var options = await ReadCurrentOptionsAsync().ConfigureAwait(false);
        var features = options["features"] as JsonObject ?? new JsonObject();
        features[featureKey] = enabled;
        options["features"] = features;

        return await WriteOptionsAsync(options, cancellationToken);
    }

    /// <summary>
    /// Posts a fully merged options document to the Supervisor self-options endpoint.
    /// </summary>
    /// <param name="options">The options document to persist.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns><see langword="true"/> when the Supervisor accepted the update.</returns>
    private async Task<bool> WriteOptionsAsync(JsonObject options, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(_supervisorToken))
        {
            _logger.LogDebug("SUPERVISOR_TOKEN is not set; skipping add-on options write.");
            return false;
        }

        var payload = JsonSerializer.Serialize(new { options });

        try
        {
            using var response = await HttpRetry.ExecuteAsync(
                async () =>
                {
                    var req = new HttpRequestMessage(HttpMethod.Post, "addons/self/options")
                    {
                        Content = new StringContent(payload, Encoding.UTF8, "application/json")
                    };
                    req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _supervisorToken);
                    return await _httpClient.SendAsync(req, cancellationToken);
                },
                3,
                HttpRetry.IsTransientResponse,
                HttpRetry.LinearBackoff,
                null,
                null,
                cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Updated add-on options via Supervisor.");
                return true;
            }

            _logger.LogWarning("Supervisor rejected the add-on options update: HTTP {StatusCode}", (int)response.StatusCode);
            return false;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Failed to update add-on options via Supervisor.");
            return false;
        }
    }
}
