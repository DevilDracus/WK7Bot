using Ical.Net;
using WK7Bot.Core.Utilities;
using WK7Bot.Services.Interfaces;

namespace WK7Bot.Services;

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

            // Strip UTF-8 BOM and leading whitespace before validating the ICS envelope.
            contentString = contentString.TrimStart('\uFEFF', ' ', '\t', '\r', '\n');

            if (!contentString.StartsWith("BEGIN:VCALENDAR", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("Retrieved payload from Leipzig waste endpoint does not appear to be a valid ICS calendar stream.");
                return detectedWasteTypes;
            }

            var calendar = Calendar.Load(contentString);
            if (calendar?.Events == null)
            {
                return detectedWasteTypes;
            }

            foreach (var calendarEvent in calendar.Events)
            {
                var startDate = calendarEvent?.Start?.Value;
                if (startDate.HasValue && startDate.Value.Date == targetDate.Date)
                {
                    var rawSummary = calendarEvent?.Summary ?? string.Empty;
                    var friendlyName = WasteSummaryMapper.MapToFriendlyName(rawSummary);
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
}