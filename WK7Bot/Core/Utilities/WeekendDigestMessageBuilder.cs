namespace WK7Bot.Core.Utilities;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Discord;
using WK7Bot.Models;

/// <summary>
/// Builds the embed and native Discord poll of the weekend digest: one field per suggestion plus a
/// three-answer poll that lets the group vote on the picks without any bot-side vote bookkeeping.
/// </summary>
public static class WeekendDigestMessageBuilder
{
    private const int EmbedTitleLimit = 256;
    private const int FieldNameLimit = 256;
    private const int FieldValueLimit = 1024;
    private const int PollQuestionLimit = 300;
    private const int PollAnswerLimit = 55;
    private const uint MaxPollDurationHours = 168;

    private static readonly CultureInfo GermanCulture = new("de-DE");

    private static readonly string[] DayAbbreviations = { "So", "Mo", "Di", "Mi", "Do", "Fr", "Sa" };

    /// <summary>
    /// Renders the digest for the weekend starting on <paramref name="weekendFriday"/>.
    /// The poll is only attached when there are at least two suggestions; a one-answer poll would be pointless.
    /// </summary>
    /// <param name="suggestions">The curated suggestions (at most three), in day order.</param>
    /// <param name="weekendFriday">The Friday date identifying the target weekend.</param>
    /// <param name="postTime">The local time the digest is posted; also anchors the poll duration.</param>
    /// <returns>The embed and the poll to attach, or a <see langword="null"/> poll for a single suggestion.</returns>
    public static (Embed Embed, PollProperties? Poll) Build(
        IReadOnlyList<WeekendEvent> suggestions,
        DateTime weekendFriday,
        DateTime postTime)
    {
        ArgumentNullException.ThrowIfNull(suggestions);

        var sunday = weekendFriday.AddDays(2);
        var embedBuilder = new EmbedBuilder()
            .WithTitle(Truncate($"📅 Wochenend-Tipps — {FormatDateRange(weekendFriday, sunday)}", EmbedTitleLimit))
            .WithDescription(BuildDescription(suggestions))
            .WithColor(Color.Gold)
            .WithTimestamp(postTime)
            .WithFooter("Datenquelle: leipzig.de");

        for (var i = 0; i < suggestions.Count; i++)
        {
            var suggestion = suggestions[i];
            embedBuilder.AddField(
                Truncate($"{i + 1}️⃣ {FormatDayLabel(suggestion.StartDate)} · {suggestion.Title}", FieldNameLimit),
                Truncate(BuildFieldValue(suggestion), FieldValueLimit),
                false);
        }

        var poll = suggestions.Count >= 2 ? BuildPoll(suggestions, postTime, sunday) : null;
        return (embedBuilder.Build(), poll);
    }

    /// <summary>
    /// Builds the poll: question, one answer per suggestion (max 55 chars) and a duration covering
    /// the remaining time until the end of the weekend, clamped to Discord's limits.
    /// </summary>
    /// <param name="suggestions">The curated suggestions.</param>
    /// <param name="postTime">The local time the digest is posted.</param>
    /// <param name="sunday">The Sunday of the target weekend.</param>
    /// <returns>The poll to attach to the digest message.</returns>
    private static PollProperties BuildPoll(IReadOnlyList<WeekendEvent> suggestions, DateTime postTime, DateTime sunday)
    {
        var answers = suggestions
            .Select(s => new PollMediaProperties { Text = Truncate(BuildAnswerText(s), PollAnswerLimit) })
            .ToList();

        return new PollProperties
        {
            Question = new PollMediaProperties
            {
                Text = Truncate("Wohin gehen wir am Wochenende?", PollQuestionLimit)
            },
            Answers = answers,
            Duration = ComputeDurationHours(postTime, sunday),
            AllowMultiselect = false
        };
    }

    /// <summary>
    /// Computes the poll duration in hours: the time from posting until the end of Sunday,
    /// clamped between one hour and Discord's maximum.
    /// </summary>
    /// <param name="postTime">The local time the digest is posted.</param>
    /// <param name="sunday">The Sunday of the target weekend.</param>
    /// <returns>The duration in hours.</returns>
    private static uint ComputeDurationHours(DateTime postTime, DateTime sunday)
    {
        var pollEnd = sunday.Date.AddDays(1).AddMinutes(-1);
        var hours = (int)Math.Ceiling((pollEnd - postTime).TotalHours);
        return (uint)Math.Clamp(hours, 1, (int)MaxPollDurationHours);
    }

    /// <summary>
    /// Builds the German intro line, adjusted to the number of suggestions.
    /// </summary>
    /// <param name="suggestions">The curated suggestions.</param>
    /// <returns>The embed description.</returns>
    private static string BuildDescription(IReadOnlyList<WeekendEvent> suggestions)
        => suggestions.Count switch
        {
            0 => "Dieses Wochenende wurden keine passenden Tipps gefunden.",
            1 => "**1 Tipp** für das Wochenende – stimmt ab, wohin wir gehen! 🗳️",
            _ => $"**{suggestions.Count} Tipps** für das Wochenende – stimmt ab, wohin wir gehen! 🗳️"
        };

    /// <summary>
    /// Builds the field value: time, location, topic and a link back to the source page.
    /// </summary>
    /// <param name="weekendEvent">The suggestion to render.</param>
    /// <returns>The formatted field value.</returns>
    private static string BuildFieldValue(WeekendEvent weekendEvent)
    {
        var lines = new List<string>
        {
            $"⏰ {weekendEvent.TimeText ?? "ganztägig"}"
        };

        if (!string.IsNullOrWhiteSpace(weekendEvent.Location))
        {
            lines.Add($"📍 {weekendEvent.Location}");
        }

        if (!string.IsNullOrWhiteSpace(weekendEvent.Topic))
        {
            lines.Add($"🏷️ {weekendEvent.Topic}");
        }

        if (!string.IsNullOrWhiteSpace(weekendEvent.Url))
        {
            lines.Add($"🔗 [Mehr Infos]({weekendEvent.Url})");
        }

        return string.Join('\n', lines);
    }

    /// <summary>
    /// Builds a compact poll answer such as <c>Sa 10:00 · Wochenmarkt</c>, fitting Discord's 55-char limit.
    /// </summary>
    /// <param name="weekendEvent">The suggestion to render.</param>
    /// <returns>The answer text.</returns>
    private static string BuildAnswerText(WeekendEvent weekendEvent)
    {
        var builder = new StringBuilder();
        builder.Append(DayAbbreviations[(int)weekendEvent.StartDate.DayOfWeek]);

        var timeText = weekendEvent.TimeText;
        if (timeText is { Length: >= 5 } && timeText[2] == ':')
        {
            builder.Append(' ').Append(timeText.Substring(0, 5));
        }

        builder.Append(" · ").Append(weekendEvent.Title);
        return builder.ToString();
    }

    /// <summary>
    /// Formats the weekend range as <c>09.–11. Oktober 2026</c>, falling back to explicit dates
    /// when the weekend spans two months.
    /// </summary>
    /// <param name="friday">The Friday of the weekend.</param>
    /// <param name="sunday">The Sunday of the weekend.</param>
    /// <returns>The formatted date range.</returns>
    private static string FormatDateRange(DateTime friday, DateTime sunday)
        => friday.Month == sunday.Month
            ? $"{friday:dd}.–{sunday:dd.} {GermanCulture.DateTimeFormat.GetMonthName(sunday.Month)} {sunday:yyyy}"
            : $"{friday:dd.MM}.–{sunday:dd.MM.yyyy}";

    /// <summary>
    /// Formats a date as <c>Fr, 09.10.</c> for embed field names.
    /// </summary>
    /// <param name="date">The date to format.</param>
    /// <returns>The formatted day label.</returns>
    private static string FormatDayLabel(DateTime date)
        => $"{DayAbbreviations[(int)date.DayOfWeek]}, {date:dd.MM.}";

    /// <summary>
    /// Shortens text to <paramref name="maxChars"/> characters, appending an ellipsis when truncated.
    /// </summary>
    /// <param name="text">The text to shorten.</param>
    /// <param name="maxChars">The maximum length.</param>
    /// <returns>The possibly truncated text.</returns>
    private static string Truncate(string text, int maxChars)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxChars)
        {
            return text;
        }

        if (maxChars <= 1)
        {
            return text.Substring(0, maxChars);
        }

        return text.Substring(0, maxChars - 1) + "…";
    }
}
