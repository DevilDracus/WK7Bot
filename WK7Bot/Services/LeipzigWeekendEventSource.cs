namespace WK7Bot.Services;

using System.Net.Http;
using System.Net.Http.Headers;
using WK7Bot.Core.Utilities;
using WK7Bot.Models;
using WK7Bot.Services.Interfaces;

/// <summary>
/// Fetches weekend events from the two leipzig.de listing pages: the "termine dieses Wochenende" page
/// (rich Saturday/Sunday coverage) and the month overview page of the target Friday (which carries the
/// Friday entries the weekend page omits). Both pages share the same card markup and are deduplicated by URL.
/// </summary>
public class LeipzigWeekendEventSource : IWeekendEventSource
{
    /// <summary>
    /// URL of the weekend listing page (Saturday and Sunday events).
    /// </summary>
    public const string WeekendListingUrl = "https://www.leipzig.de/kultur-und-freizeit/veranstaltungen/termine-dieses-wochenende";

    private const string MonthListingUrlFormat = "https://www.leipzig.de/kultur-und-freizeit/veranstaltungen/{0}";

    private const string UserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36";

    private static readonly string[] MonthSlugs =
    {
        "januar", "februar", "maerz", "april", "mai", "juni",
        "juli", "august", "september", "oktober", "november", "dezember"
    };

    private readonly HttpClient _httpClient;
    private readonly ILogger<LeipzigWeekendEventSource> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="LeipzigWeekendEventSource"/> class.
    /// </summary>
    /// <param name="httpClient">The HTTP client used for the listing requests.</param>
    /// <param name="logger">The logger instance for fetch and parse diagnostics.</param>
    public LeipzigWeekendEventSource(HttpClient httpClient, ILogger<LeipzigWeekendEventSource> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Builds the URL of the month overview page covering the given Friday.
    /// </summary>
    /// <param name="weekendFriday">The Friday date identifying the target weekend.</param>
    /// <returns>The absolute month listing URL.</returns>
    public static string BuildMonthUrl(DateTime weekendFriday)
        => string.Format(MonthListingUrlFormat, MonthSlugs[weekendFriday.Month - 1]);

    /// <summary>
    /// Downloads both listing pages, parses their event cards, merges them and keeps the events that
    /// overlap Friday through Sunday. When no page could be fetched at all the request fails, so the
    /// caller can retry instead of treating a network outage as an empty weekend.
    /// </summary>
    /// <param name="weekendFriday">The Friday date identifying the target weekend.</param>
    /// <param name="cancellationToken">Cancellation token for network operations.</param>
    /// <returns>The deduplicated events of that weekend.</returns>
    public async Task<IReadOnlyList<WeekendEvent>> FetchWeekendEventsAsync(DateTime weekendFriday, CancellationToken cancellationToken = default)
    {
        var urls = new[] { WeekendListingUrl, BuildMonthUrl(weekendFriday) };
        var events = new List<WeekendEvent>();
        var seenUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var fetchedAny = false;

        foreach (var url in urls)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string html;
            try
            {
                html = await DownloadAsync(url, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Failed to fetch weekend listing {Url}.", url);
                continue;
            }

            fetchedAny = true;
            foreach (var weekendEvent in WeekendEventParser.Parse(html))
            {
                if (seenUrls.Add(weekendEvent.Url))
                {
                    events.Add(weekendEvent);
                }
            }
        }

        if (!fetchedAny)
        {
            throw new HttpRequestException("No leipzig.de weekend listing could be fetched.");
        }

        var sunday = weekendFriday.AddDays(2);
        var inWeekend = events
            .Where(e => e.StartDate <= sunday && e.EndDate >= weekendFriday)
            .OrderBy(e => e.StartDate)
            .ThenBy(e => e.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();

        _logger.LogInformation(
            "Parsed {Count} weekend event(s) for {Friday:dd.MM.yyyy} ({Total} total listings).",
            inWeekend.Count,
            weekendFriday,
            events.Count);

        return inWeekend;
    }

    /// <summary>
    /// Downloads a listing page with a browser-like user agent.
    /// </summary>
    /// <param name="url">The absolute listing URL.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>The raw HTML content.</returns>
    private async Task<string> DownloadAsync(string url, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd(UserAgent);
        request.Headers.Accept.ParseAdd("text/html");

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }
}
