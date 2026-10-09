using Ical.Net;
using Microsoft.Extensions.Caching.Memory;
using WK7Bot.Core.Utilities;
using WK7Bot.Services.Interfaces;

namespace WK7Bot.Services;

/// <summary>
/// Implementation of the waste service responsible for retrieving and mapping ICS feed data.
/// Successfully downloaded calendars are cached briefly so repeated lookups (weekly overviews,
/// /check-waste ranges) reuse a single HTTP download instead of re-fetching the feed per date.
/// </summary>
public class LeipzigWasteService : ILeipzigWasteService
{
    private const string IcsCacheKey = "leipzig_waste_ics_content_v1";
    private static readonly TimeSpan IcsCacheTtl = TimeSpan.FromHours(1);

    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly IMemoryCache _cache;
    private readonly ILogger<LeipzigWasteService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="LeipzigWasteService"/> class.
    /// </summary>
    /// <param name="httpClient">The HTTP client instance for requesting remote web resources.</param>
    /// <param name="configuration">The configuration provider containing application settings.</param>
    /// <param name="cache">The memory cache instance used to reuse downloaded ICS payloads.</param>
    /// <param name="logger">The logger instance for diagnostics and execution logging.</param>
    /// <exception cref="ArgumentNullException">Thrown when any required dependency is null.</exception>
    public LeipzigWasteService(
        HttpClient httpClient,
        IConfiguration configuration,
        IMemoryCache cache,
        ILogger<LeipzigWasteService> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Downloads (or reuses a cached copy of) the ICS feed to retrieve waste collection summaries for a target calendar date.
    /// </summary>
    /// <param name="targetDate">The target date to evaluate against calendar events.</param>
    /// <param name="cancellationToken">Cancellation token for network request execution.</param>
    /// <returns>A list of user-friendly waste collection descriptions scheduled for the given date.</returns>
    public async Task<List<string>> GetWasteTypesForDateAsync(DateTime targetDate, CancellationToken cancellationToken = default)
    {
        var detectedWasteTypes = new List<string>();

        // Root key (config.yaml / /data/options.json) overrides the appsettings default so the
        // add-on's private street address can be replaced without rebuilding the image.
        var feedUrl = _configuration["leipzig_waste_ics_feed_url"];
        if (string.IsNullOrWhiteSpace(feedUrl))
        {
            feedUrl = _configuration["LeipzigWaste:IcsFeedUrl"];
        }

        if (string.IsNullOrWhiteSpace(feedUrl))
        {
            _logger.LogError("Stadtreinigung Leipzig ICS feed URL is not configured (leipzig_waste_ics_feed_url).");
            return detectedWasteTypes;
        }

        var contentString = await DownloadIcsContentAsync(feedUrl, cancellationToken);
        if (contentString == null)
        {
            return detectedWasteTypes;
        }

        try
        {
            var calendar = Calendar.Load(contentString);
            if (calendar?.Events == null)
            {
                return detectedWasteTypes;
            }

            foreach (var calendarEvent in calendar.Events)
            {
                var startDate = calendarEvent?.Start?.Value;
                if (!startDate.HasValue)
                {
                    continue;
                }

                // Match collections spanning the target day too: an event is active on the day when
                // it starts on/before that day and ends after the day's start (an event without an
                // explicit end covers its start day only).
                var dayStart = targetDate.Date;
                var endDate = calendarEvent?.End?.Value ?? startDate.Value.Date.AddDays(1);

                if (startDate.Value.Date <= dayStart && endDate > dayStart)
                {
                    var rawSummary = calendarEvent?.Summary ?? string.Empty;
                    var friendlyName = WasteSummaryMapper.MapToFriendlyName(rawSummary);
                    detectedWasteTypes.Add(friendlyName);
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to parse the Stadtreinigung Leipzig ICS calendar for {TargetDate}", targetDate);
        }

        return detectedWasteTypes;
    }

    /// <summary>
    /// Retrieves the raw ICS calendar payload from the configured feed URL, reusing a cached copy when fresh.
    /// Failures are logged and never cached, so the next call retries the download.
    /// </summary>
    /// <param name="feedUrl">The configured ICS feed URL.</param>
    /// <param name="cancellationToken">Cancellation token for network request execution.</param>
    /// <returns>The BOM-trimmed ICS payload, or <see langword="null"/> when the download or validation failed.</returns>
    private async Task<string?> DownloadIcsContentAsync(string feedUrl, CancellationToken cancellationToken)
    {
        // The URL belongs to the cache key: a changed leipzig_waste_ics_feed_url must not keep
        // serving the previous calendar for the rest of the TTL.
        var cacheKey = $"{IcsCacheKey}_{feedUrl}";

        if (_cache.TryGetValue<string>(cacheKey, out var cached) && !string.IsNullOrEmpty(cached))
        {
            return cached;
        }

        try
        {
            using var response = await _httpClient.GetAsync(feedUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();

            var contentString = await response.Content.ReadAsStringAsync(cancellationToken);

            // Strip UTF-8 BOM and leading whitespace before validating the ICS envelope.
            contentString = contentString.TrimStart('\uFEFF', ' ', '\t', '\r', '\n');

            if (!contentString.StartsWith("BEGIN:VCALENDAR", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("Retrieved payload from Leipzig waste endpoint does not appear to be a valid ICS calendar stream.");
                return null;
            }

            _cache.Set(cacheKey, contentString, IcsCacheTtl);
            return contentString;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to download the Stadtreinigung Leipzig ICS feed from {FeedUrl}", feedUrl);
            return null;
        }
    }
}