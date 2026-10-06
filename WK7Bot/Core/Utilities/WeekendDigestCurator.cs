namespace WK7Bot.Core.Utilities;

using System;
using System.Collections.Generic;
using System.Linq;
using WK7Bot.Models;

/// <summary>
/// Selects the weekend digest suggestions from the raw event pool: prioritises markets (weekly, flea,
/// night and street-food markets), spreads the picks over Friday to Sunday and avoids repeating venues.
/// </summary>
public static class WeekendDigestCurator
{
    /// <summary>
    /// Default number of suggestions in a digest.
    /// </summary>
    public const int DefaultSuggestionCount = 3;

    /// <summary>
    /// Title keywords marking the market-style events the group asked for, ordered by descending weight.
    /// </summary>
    private static readonly (string Keyword, int Weight)[] TitleKeywords =
    {
        ("wochenmarkt", 100),
        ("flohmarkt", 95),
        ("nachtmarkt", 95),
        ("abendmarkt", 95),
        ("street food", 90),
        ("streetfood", 90),
        ("trödelmarkt", 90),
        ("strassenmarkt", 90),
        ("straßenmarkt", 90),
        ("foodtruck", 85),
        ("markt", 60),
        ("messe", 40)
    };

    /// <summary>
    /// Topic keywords boosting otherwise culture-only events that fit a relaxed weekend outing.
    /// </summary>
    private static readonly (string Keyword, int Weight)[] TopicKeywords =
    {
        ("messen", 50),
        ("markt", 40),
        ("kunsthandwerk", 35),
        ("festival", 35),
        ("sport", 25)
    };

    /// <summary>
    /// Picks up to <paramref name="count"/> suggestions for the weekend starting on
    /// <paramref name="weekendFriday"/>. Picks prefer one event per day, avoid venues already chosen
    /// and are ordered Friday → Sunday.
    /// </summary>
    /// <param name="events">The candidate events (already filtered to the weekend).</param>
    /// <param name="weekendFriday">The Friday date identifying the target weekend.</param>
    /// <param name="count">The maximum number of suggestions.</param>
    /// <returns>The selected suggestions in day order; may contain fewer than <paramref name="count"/> entries.</returns>
    public static IReadOnlyList<WeekendEvent> PickSuggestions(
        IEnumerable<WeekendEvent> events,
        DateTime weekendFriday,
        int count = DefaultSuggestionCount)
    {
        ArgumentNullException.ThrowIfNull(events);

        if (count <= 0)
        {
            return Array.Empty<WeekendEvent>();
        }

        var sunday = weekendFriday.AddDays(2);
        var scored = events
            .Where(e => e.StartDate <= sunday && e.EndDate >= weekendFriday)
            .Select(e => (Event: e, Score: Score(e)))
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Event.StartDate)
            .ThenBy(x => x.Event.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var picked = new List<WeekendEvent>();
        var usedVenues = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var preferredDays = new[] { weekendFriday, weekendFriday.AddDays(1), weekendFriday.AddDays(2) };

        foreach (var day in preferredDays)
        {
            if (picked.Count >= count)
            {
                break;
            }

            var candidate = scored.FirstOrDefault(x =>
                x.Event.StartDate == day &&
                !IsVenueUsed(x.Event, usedVenues) &&
                !picked.Contains(x.Event));

            if (candidate.Event is not null)
            {
                MarkVenue(candidate.Event, usedVenues);
                picked.Add(candidate.Event);
            }
        }

        foreach (var (candidate, _) in scored)
        {
            if (picked.Count >= count)
            {
                break;
            }

            if (picked.Contains(candidate) || IsVenueUsed(candidate, usedVenues))
            {
                continue;
            }

            MarkVenue(candidate, usedVenues);
            picked.Add(candidate);
        }

        return picked
            .OrderBy(e => e.StartDate)
            .ThenByDescending(Score)
            .ThenBy(e => e.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Determines whether the event's venue was already used by a previous pick; events without a
    /// location are never treated as venue duplicates.
    /// </summary>
    /// <param name="weekendEvent">The candidate event.</param>
    /// <param name="usedVenues">The venues of the picks made so far.</param>
    /// <returns><see langword="true"/> when the venue is already taken.</returns>
    private static bool IsVenueUsed(WeekendEvent weekendEvent, HashSet<string> usedVenues)
        => weekendEvent.Location is not null && usedVenues.Contains(weekendEvent.Location);

    /// <summary>
    /// Records the event's venue among the taken venues (events without a location are ignored).
    /// </summary>
    /// <param name="weekendEvent">The picked event.</param>
    /// <param name="usedVenues">The venues of the picks made so far.</param>
    private static void MarkVenue(WeekendEvent weekendEvent, HashSet<string> usedVenues)
    {
        if (weekendEvent.Location is not null)
        {
            usedVenues.Add(weekendEvent.Location);
        }
    }

    /// <summary>
    /// Computes the ranking weight of an event: market-style titles score highest, matching topics add a bonus.
    /// </summary>
    /// <param name="weekendEvent">The event to score.</param>
    /// <returns>The combined keyword weight.</returns>
    private static int Score(WeekendEvent weekendEvent)
    {
        var title = weekendEvent.Title.ToLowerInvariant();
        var score = TitleKeywords
            .Where(k => title.Contains(k.Keyword, StringComparison.Ordinal))
            .Sum(k => k.Weight);

        if (!string.IsNullOrWhiteSpace(weekendEvent.Topic))
        {
            var topic = weekendEvent.Topic.ToLowerInvariant();
            score += TopicKeywords
                .Where(k => topic.Contains(k.Keyword, StringComparison.Ordinal))
                .Sum(k => k.Weight);
        }

        return score;
    }
}
