namespace WK7Bot.Services;

using Ical.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

/// <summary>
/// Implementation of the waste service responsible for retrieving and mapping ICS feed data.
/// </summary>
public class LeipzigWasteService : ILeipzigWasteService
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<LeipzigWasteService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="LeipzigWasteService"/> class.
    /// </summary>
    /// <param name="httpClient">The HTTP client instance for requesting remote web resources.</param>
    /// <param name="configuration">The configuration provider containing application settings.</param>
    /// <param name="logger">The logger instance for diagnostics and execution logging.</param>
    public LeipzigWasteService(
        HttpClient httpClient,
        IConfiguration configuration,
        ILogger<LeipzigWasteService> logger)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>
    /// Downloads and parses the ICS feed to retrieve waste collection summaries for a target calendar date.
    /// </summary>
    /// <param name="targetDate">The target date to evaluate against calendar events.</param>
    /// <param name="cancellationToken">Cancellation token for network request execution.</param>
    /// <returns>A list of user-friendly waste collection descriptions scheduled for the given date.</returns>
    public async Task<List<string>> GetWasteTypesForDateAsync(DateTime targetDate, CancellationToken cancellationToken = default)
    {
        var detectedWasteTypes = new List<string>();
        var feedUrl = _configuration["LeipzigWaste:IcsFeedUrl"];

        if (string.IsNullOrWhiteSpace(feedUrl))
        {
            _logger.LogError("Stadtreinigung Leipzig ICS feed URL is not configured in appsettings.json.");
            return detectedWasteTypes;
        }

        try
        {
            using var response = await _httpClient.GetAsync(feedUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();

            var contentString = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!contentString.StartsWith("BEGIN:VCALENDAR", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("Retrieved payload from Leipzig waste endpoint does not appear to be a valid ICS calendar stream.");
                return detectedWasteTypes;
            }

            var calendar = Calendar.Load(contentString);

            foreach (var calendarEvent in calendar.Events)
            {
                if (calendarEvent.Start.Value.Date == targetDate.Date)
                {
                    var friendlyName = MapWasteSummaryToFriendlyName(calendarEvent.Summary);
                    detectedWasteTypes.Add(friendlyName);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to download or parse the Stadtreinigung Leipzig ICS feed from {FeedUrl}", feedUrl);
        }

        return detectedWasteTypes;
    }

    /// <summary>
    /// Maps raw ICS event summaries into user-friendly German display strings with matching emojis.
    /// </summary>
    /// <param name="rawSummary">The raw text summary extracted from the calendar event.</param>
    /// <returns>A formatted string with emoji formatting for display.</returns>
    private static string MapWasteSummaryToFriendlyName(string rawSummary)
    {
        if (rawSummary.Contains("Restabfall", StringComparison.OrdinalIgnoreCase) || rawSummary.Contains("schwarz", StringComparison.OrdinalIgnoreCase))
        {
            return "⬛ Schwarze Tonne (Restabfall)";
        }
        if (rawSummary.Contains("Papier", StringComparison.OrdinalIgnoreCase) || rawSummary.Contains("blau", StringComparison.OrdinalIgnoreCase))
        {
            return "🟦 Blaue Tonne (Pappe & Papier)";
        }
        if (rawSummary.Contains("Wertstoff", StringComparison.OrdinalIgnoreCase) || rawSummary.Contains("gelb", StringComparison.OrdinalIgnoreCase))
        {
            return "🟨 Gelbe Tonne / Gelber Sack (Wertstoffe)";
        }
        if (rawSummary.Contains("Bio", StringComparison.OrdinalIgnoreCase) || rawSummary.Contains("braun", StringComparison.OrdinalIgnoreCase))
        {
            return "🟫 Braune Tonne (Biogut)";
        }

        return $"🗑️ {rawSummary}";
    }
}