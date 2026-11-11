namespace WK7Bot.Services;

using System.Globalization;
using System.Text.Json;
using WK7Bot.Core.Utilities;
using WK7Bot.Services.Interfaces;

/// <summary>
/// Client for the public Steam store search and appdetails endpoints.
/// </summary>
public sealed class SteamStoreService : ISteamStoreService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<SteamStoreService> _logger;

    /// <summary>Initializes a new instance of the <see cref="SteamStoreService"/> class.</summary>
    /// <param name="httpClient">The HTTP client for Steam store requests.</param>
    /// <param name="logger">The logger instance.</param>
    public SteamStoreService(HttpClient httpClient, ILogger<SteamStoreService> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<(uint AppId, string Title)?> SearchGameAsync(string gameName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(gameName))
        {
            return null;
        }

        var url = $"https://store.steampowered.com/api/storesearch/?term={Uri.EscapeDataString(gameName)}&cc=de&l=german";
        try
        {
            using var response = await HttpRetry.ExecuteAsync(
                () => _httpClient.GetAsync(url, cancellationToken),
                3,
                HttpRetry.IsTransientResponse,
                HttpRetry.LinearBackoff,
                null,
                null,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
            if (!document.RootElement.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var first = items.EnumerateArray().FirstOrDefault();
            if (first.ValueKind != JsonValueKind.Object || !first.TryGetProperty("id", out var id) || !first.TryGetProperty("name", out var name))
            {
                return null;
            }

            return (id.GetUInt32(), name.GetString() ?? gameName);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Steam store search failed for {Game}.", gameName);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<SteamStoreAppDetails?> GetAppDetailsAsync(uint appId, CancellationToken cancellationToken = default)
    {
        var url = $"https://store.steampowered.com/api/appdetails?appids={appId}&cc=de&l=german";
        try
        {
            using var response = await HttpRetry.ExecuteAsync(
                () => _httpClient.GetAsync(url, cancellationToken),
                3,
                HttpRetry.IsTransientResponse,
                HttpRetry.LinearBackoff,
                null,
                null,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
            if (!document.RootElement.TryGetProperty(appId.ToString(CultureInfo.InvariantCulture), out var app) ||
                !app.TryGetProperty("success", out var success) ||
                success.GetBoolean() != true ||
                !app.TryGetProperty("data", out var data))
            {
                return null;
            }

            var name = data.TryGetProperty("name", out var nameProp) ? nameProp.GetString() ?? string.Empty : string.Empty;
            var isComingSoon = false;
            var isTba = false;
            DateTime? releaseDate = null;

            if (data.TryGetProperty("release_date", out var releaseDateObj))
            {
                isComingSoon = releaseDateObj.TryGetProperty("coming_soon", out var comingSoonProp) && comingSoonProp.GetBoolean();

                if (releaseDateObj.TryGetProperty("date", out var dateProp) && dateProp.ValueKind == JsonValueKind.String)
                {
                    var dateText = dateProp.GetString() ?? string.Empty;
                    isTba = string.IsNullOrWhiteSpace(dateText) ||
                        dateText.Contains("TBA", StringComparison.OrdinalIgnoreCase) ||
                        dateText.Contains("To Be Announced", StringComparison.OrdinalIgnoreCase);

                    if (!isTba && DateTime.TryParseExact(dateText, "dd MMM, yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
                    {
                        releaseDate = parsed;
                    }
                    else if (!isTba && DateTime.TryParse(dateText, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedFallback))
                    {
                        releaseDate = parsedFallback;
                    }
                    else if (!isTba)
                    {
                        // Could not parse a non-TBA date; treat it as TBA to avoid spurious notifications.
                        isTba = true;
                    }
                }
                else
                {
                    isTba = true;
                }
            }
            else
            {
                isTba = true;
            }

            return new SteamStoreAppDetails(appId, name, releaseDate, isTba, isComingSoon);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Steam appdetails request failed for {AppId}.", appId);
            return null;
        }
    }
}
