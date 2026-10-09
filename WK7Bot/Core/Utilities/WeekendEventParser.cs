namespace WK7Bot.Core.Utilities;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using WK7Bot.Models;

/// <summary>
/// Extracts event cards from the server-rendered leipzig.de listing HTML (weekend and month pages),
/// which share the same <c>&lt;li data-event&gt;</c> → <c>article.event-card</c> markup.
/// </summary>
public static class WeekendEventParser
{
    private const string BaseUrl = "https://www.leipzig.de";

    /// <summary>
    /// Matches one listing item, i.e. an <c>&lt;li&gt;</c> carrying a <c>data-event</c> id.
    /// </summary>
    private static readonly Regex ItemRegex = new(
        @"<li\b[^>]*\bdata-event=""[^""]*""[^>]*>(?<card>.*?)</li>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex LinkRegex = new(
        @"<a\b[^>]*\bhref\s*=\s*""(?<href>[^""]+)""",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex TitleRegex = new(
        @"<h3[^>]*>(?<title>.*?)</h3>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    /// <summary>
    /// Matches the plain value span following an icon label such as <c>event</c>, <c>location_on</c> or <c>topic</c>.
    /// </summary>
    private static readonly Regex ValueAfterIconRegex = new(
        @"aria-hidden=""true"">(?<icon>event|location_on|topic)</span>\s*<span>\s*(?<value>.*?)\s*</span>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    /// <summary>
    /// Matches the leading date plus optional time parts of an event row, e.g.
    /// <c>09.10.2026 · 09:00 – 17:40 Uhr</c> or <c>03.10.2026 – 04.10.2026</c>.
    /// </summary>
    private static readonly Regex DateLineRegex = new(
        @"^\s*(?<start>\d{2}\.\d{2}\.\d{4})(?:\s*·\s*(?<time1>\d{2}:\d{2})(?:\s*[–-]\s*(?<time2>\d{2}:\d{2}))?(?:\s*Uhr)?)?(?:\s*[–-]\s*(?<end>\d{2}\.\d{2}\.\d{4}))?\s*$",
        RegexOptions.Compiled);

    private static readonly Regex TagRegex = new(
        "<[^>]+>",
        RegexOptions.Compiled);

    /// <summary>
    /// Parses all event cards contained in the given listing HTML.
    /// Cards without a readable date are skipped; values are HTML-decoded and tags stripped.
    /// </summary>
    /// <param name="html">The raw page content of a leipzig.de listing page.</param>
    /// <returns>The parsed events in page order.</returns>
    public static IReadOnlyList<WeekendEvent> Parse(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return Array.Empty<WeekendEvent>();
        }

        var events = new List<WeekendEvent>();

        foreach (Match item in ItemRegex.Matches(html))
        {
            var card = item.Groups["card"].Value;
            var title = ExtractTitle(card);
            var link = ExtractLink(card);
            if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(link))
            {
                continue;
            }

            string? dateRow = null;
            string? location = null;
            string? topic = null;

            foreach (Match row in ValueAfterIconRegex.Matches(card))
            {
                var value = Clean(row.Groups["value"].Value);
                switch (row.Groups["icon"].Value.ToLowerInvariant())
                {
                    case "event" when dateRow is null:
                        dateRow = value;
                        break;
                    case "location_on" when location is null:
                        location = value;
                        break;
                    case "topic" when topic is null:
                        topic = value;
                        break;
                }
            }

            if (dateRow is null || !TryParseDateLine(dateRow, out var startDate, out var endDate, out var timeText))
            {
                continue;
            }

            events.Add(new WeekendEvent
            {
                Title = title,
                StartDate = startDate,
                EndDate = endDate ?? startDate,
                TimeText = timeText,
                Location = string.IsNullOrWhiteSpace(location) ? null : location,
                Topic = string.IsNullOrWhiteSpace(topic) ? null : topic,
                Url = NormalizeUrl(link)
            });
        }

        return events;
    }

    /// <summary>
    /// Parses the event row text into start date, optional multi-day end date and normalized time text.
    /// </summary>
    /// <param name="text">The decoded <c>event</c> row value.</param>
    /// <param name="startDate">The parsed start date.</param>
    /// <param name="endDate">The parsed end date for multi-day events; <see langword="null"/> when single-day.</param>
    /// <param name="timeText">The normalized time part (e.g. <c>09:00 – 17:40 Uhr</c>) or <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when a start date could be parsed.</returns>
    private static bool TryParseDateLine(
        string text,
        out DateTime startDate,
        out DateTime? endDate,
        out string? timeText)
    {
        startDate = default;
        endDate = null;
        timeText = null;

        var match = DateLineRegex.Match(text);
        if (!match.Success ||
            !DateTime.TryParseExact(
                match.Groups["start"].Value,
                "dd.MM.yyyy",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out startDate))
        {
            return false;
        }

        if (match.Groups["end"].Success &&
            DateTime.TryParseExact(
                match.Groups["end"].Value,
                "dd.MM.yyyy",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var parsedEnd))
        {
            endDate = parsedEnd;
        }

        if (match.Groups["time1"].Success)
        {
            timeText = match.Groups["time2"].Success
                ? $"{match.Groups["time1"].Value} – {match.Groups["time2"].Value} Uhr"
                : $"{match.Groups["time1"].Value} Uhr";
        }

        return true;
    }

    /// <summary>
    /// Extracts and decodes the event title from the card markup.
    /// </summary>
    private static string? ExtractTitle(string card)
    {
        var match = TitleRegex.Match(card);
        return match.Success ? Clean(match.Groups["title"].Value) : null;
    }

    /// <summary>
    /// Extracts the detail page link from the card markup.
    /// </summary>
    private static string? ExtractLink(string card)
    {
        var match = LinkRegex.Match(card);
        return match.Success ? WebUtility.HtmlDecode(match.Groups["href"].Value.Trim()) : null;
    }

    /// <summary>
    /// Strips residual markup and decodes HTML entities of a card value.
    /// </summary>
    private static string Clean(string value)
        => WebUtility.HtmlDecode(TagRegex.Replace(value, string.Empty)).Trim();

    /// <summary>
    /// Converts a root-relative or protocol-relative listing link into an absolute URL.
    /// </summary>
    private static string NormalizeUrl(string href)
    {
        if (href.StartsWith("//", StringComparison.Ordinal))
        {
            return "https:" + href;
        }

        return href.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? href : BaseUrl + href;
    }
}
