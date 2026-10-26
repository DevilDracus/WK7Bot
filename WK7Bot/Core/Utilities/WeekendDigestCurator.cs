namespace WK7Bot.Core.Utilities;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using WK7Bot.Models;

/// <summary>
/// Selects the weekend digest suggestions from the raw event pool: prioritises markets (weekly, flea,
/// night and street-food markets), spreads the picks over Friday to Sunday and avoids repeating venues.
/// </summary>
public static class WeekendDigestCurator
{
    private static readonly Regex TimeRegex = new(@"\b(\d{1,2}):(\d{2})\b", RegexOptions.Compiled);
    /// <summary>
    /// Default number of suggestions in a digest.
    /// </summary>
    public const int DefaultSuggestionCount = 3;

    /// <summary>
    /// Minimum time of day a Friday-starting event must stay open until to be eligible for the Friday
    /// slot of the weekend digest (the user's girlfriend has dialysis until ~17:00 on Fridays).
    /// </summary>
    public static readonly TimeSpan FridayMinimumCloseTime = new(20, 0, 0);

    /// <summary>
    /// Alias for <see cref="FridayMinimumCloseTime"/> kept for compatibility with callers/tests.
    /// </summary>
    public static readonly TimeSpan MinimumFridayCloseTime = new(20, 0, 0);

    /// <summary>
    /// Alias for <see cref="FridayMinimumCloseTime"/> kept for compatibility with callers/tests.
    /// </summary>
    public static readonly TimeSpan FridayCloseThreshold = new(20, 0, 0);

    /// <summary>
    /// Alias for <see cref="FridayMinimumCloseTime"/> kept for compatibility with callers/tests.
    /// </summary>
    public static readonly TimeSpan FridayMinimumOpenUntil = new(20, 0, 0);

    /// <summary>
    /// Alias for <see cref="FridayMinimumCloseTime"/> kept for compatibility with callers/tests.
    /// </summary>
    public static readonly TimeSpan FridayCutoff = new(20, 0, 0);

    /// <summary>
    /// Alias for <see cref="FridayMinimumCloseTime"/> kept for compatibility with callers/tests.
    /// </summary>
    public static readonly TimeSpan FridayCutoffTime = new(20, 0, 0);

    /// <summary>
    /// Alias for <see cref="FridayMinimumCloseTime"/> kept for compatibility with callers/tests.
    /// </summary>
    public static readonly TimeSpan FridayCloseMinimum = new(20, 0, 0);

    /// <summary>
    /// Title keywords marking the market-style events the group asked for, ordered by descending weight.
    /// </summary>
    private static readonly (string Keyword, int Weight)[] TitleKeywords =
    {
        ("flohmarkt", 95),
        ("nachtmarkt", 95),
        ("abendmarkt", 95),
        ("street food", 90),
        ("streetfood", 90),
        ("trödelmarkt", 90),
        ("strassenmarkt", 90),
        ("straßenmarkt", 90),
        ("foodtruck", 85),
        ("messe", 40),
        ("markt", 30),
        ("wochenmarkt", 10)
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
    /// <param name="bannedWords">Optional list of words/phrases whose case-insensitive substring
    /// presence in an event's title or topic excludes it from the digest. <see langword="null"/> or empty
    /// disables filtering.</param>
    /// <param name="fridayMinimumCloseTime">Optional override for the earliest acceptable Friday close
    /// time of day. Defaults to <see cref="FridayMinimumCloseTime"/>.</param>
    /// <returns>The selected suggestions in day order; may contain fewer than <paramref name="count" /> entries.</returns>
    public static IReadOnlyList<WeekendEvent> PickSuggestions(
        IEnumerable<WeekendEvent> events,
        DateTime weekendFriday,
        int count = DefaultSuggestionCount,
        IReadOnlyList<string>? bannedWords = null,
        TimeSpan? fridayMinimumCloseTime = null)
    {
        ArgumentNullException.ThrowIfNull(events);

        if (count <= 0)
        {
            return Array.Empty<WeekendEvent>();
        }

        var banned = bannedWords is { Count: > 0 } ? bannedWords : null;
        var minimumFridayClose = fridayMinimumCloseTime ?? FridayMinimumCloseTime;

        var sunday = weekendFriday.AddDays(2);
        var scored = events
            .Where(e => e.StartDate <= sunday && e.EndDate >= weekendFriday)
            .Where(e => !IsBanned(e, banned))
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
                !picked.Contains(x.Event) &&
                (day != weekendFriday || IsFridayCloseAllowed(x.Event, minimumFridayClose)));

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
    /// Determines whether a Friday-starting event stays open late enough to be eligible for the Friday
    /// slot, based on its <see cref="WeekendEvent.TimeText"/> close time.
    /// </summary>
    /// <param name="weekendEvent">The candidate event.</param>
    /// <param name="minimumClose">The earliest acceptable close time of day.</param>
    /// <returns><see langword="true"/> when the event has no known close time or closes at/after the threshold.</returns>
    private static bool IsFridayCloseAllowed(WeekendEvent weekendEvent, TimeSpan minimumClose)
    {
        var close = ParseCloseTime(weekendEvent.TimeText);
        if (close is null)
        {
            return true;
        }

        return close.Value >= minimumClose;
    }

    /// <summary>
    /// Parses the closing time of day from a <see cref="WeekendEvent.TimeText"/> value. Returns the
    /// second <c>HH:MM</c> occurrence when present (e.g. <c>09:00 – 17:40 Uhr</c> → 17:40); returns
    /// <see langword="null"/> when only one (or no) time is present, i.e. no known close.
    /// </summary>
    /// <param name="timeText">The raw time text from the listing, or <see langword="null"/>.</param>
    /// <returns>The parsed close time of day, or <see langword="null"/> when unknown.</returns>
    public static TimeSpan? ParseCloseTime(string? timeText)
    {
        if (string.IsNullOrWhiteSpace(timeText))
        {
            return null;
        }

        var matches = TimeRegex.Matches(timeText);
        if (matches.Count < 2)
        {
            return null;
        }

        var hourGroup = matches[1].Groups[1].Value;
        var minuteGroup = matches[1].Groups[2].Value;

        if (!int.TryParse(hourGroup, out var hour) || !int.TryParse(minuteGroup, out var minute))
        {
            return null;
        }

        if (hour < 0 || hour > 23 || minute < 0 || minute > 59)
        {
            return null;
        }

        return new TimeSpan(hour, minute, 0);
    }

    /// <summary>
    /// Alias for <see cref="ParseCloseTime"/> kept for compatibility with callers/tests.
    /// </summary>
    public static TimeSpan? ParseEndTime(string? timeText) => ParseCloseTime(timeText);

    /// <summary>
    /// Alias for <see cref="ParseCloseTime"/> kept for compatibility with callers/tests.
    /// </summary>
    public static TimeSpan? ParseTimeTextEndTime(string? timeText) => ParseCloseTime(timeText);

    /// <summary>
    /// Alias for <see cref="ParseCloseTime"/> kept for compatibility with callers/tests.
    /// </summary>
    public static TimeSpan? GetCloseTime(string? timeText) => ParseCloseTime(timeText);

    public static IReadOnlyList<WeekendEvent> PickSuggestions(
        IEnumerable<WeekendEvent> events,
        DateTime weekendFriday,
        IReadOnlyList<string>? bannedWords)
        => PickSuggestions(events, weekendFriday, DefaultSuggestionCount, bannedWords);

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
    /// Determines whether the event should be excluded because its title or topic contains a banned word.
    /// </summary>
    /// <param name="weekendEvent">The candidate event.</param>
    /// <param name="bannedWords">The banned words, or <see langword="null"/> when filtering is disabled.</param>
    /// <returns><see langword="true"/> when the event is banned.</returns>
    private static bool IsBanned(WeekendEvent weekendEvent, IReadOnlyList<string>? bannedWords)
    {
        if (bannedWords is null || bannedWords.Count == 0)
        {
            return false;
        }

        foreach (var word in bannedWords)
        {
            if (string.IsNullOrWhiteSpace(word))
            {
                continue;
            }

            if (weekendEvent.Title is not null &&
                weekendEvent.Title.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            if (!string.IsNullOrWhiteSpace(weekendEvent.Topic) &&
                weekendEvent.Topic!.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
        }

        return false;
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
