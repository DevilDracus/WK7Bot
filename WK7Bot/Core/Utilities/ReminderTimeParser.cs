namespace WK7Bot.Core.Utilities;

using System.Globalization;
using System.Text.RegularExpressions;

/// <summary>
/// Parses the <c>wann</c> argument of <c>/remind</c>. Supported forms are a relative offset
/// (<c>in 30m</c>, <c>in 2 h</c>, <c>1d</c>), a clock time (<c>18:30</c>, which rolls over to
/// tomorrow when it already passed) and a day with a clock time (<c>morgen 08:00</c>,
/// <c>übermorgen 20:00</c>). A bare day defaults to 08:00.
/// </summary>
public static class ReminderTimeParser
{
    /// <summary>Shortest allowed lead time.</summary>
    public static readonly TimeSpan MinimumLead = TimeSpan.FromMinutes(1);

    /// <summary>Longest allowed lead time.</summary>
    public static readonly TimeSpan MaximumLead = TimeSpan.FromDays(365);

    private static readonly Regex RelativePattern = new(
        @"^(?:in\s+)?(?<n>\d+)\s*(?<u>s|sek|sekunden|m|min|minuten|h|std|stunde|stunden|d|t|tag|tage|w|woche|wochen)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ClockTimePattern = new(
        @"^(?:(?<day>heute|morgen|übermorgen|uebermorgen)\s+)?(?<h>\d{1,2}):(?<m>\d{2})$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex DayOnlyPattern = new(
        @"^(?<day>heute|morgen|übermorgen|uebermorgen)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Parses the reminder time relative to <paramref name="now"/>.
    /// </summary>
    /// <param name="input">The raw user input.</param>
    /// <param name="now">The current local time.</param>
    /// <param name="dueAt">The resolved local due time.</param>
    /// <param name="error">A user-facing German error message, or <see langword="null"/> on success.</param>
    /// <returns><see langword="true"/> when the input was understood.</returns>
    public static bool TryParse(string? input, DateTime now, out DateTime dueAt, out string? error)
    {
        dueAt = default;
        error = null;

        var text = (input ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            error = "Bitte gib eine Zeit an, z. B. `in 30m`, `18:30` oder `morgen 08:00`.";
            return false;
        }

        if (!TryResolve(text, now, out dueAt))
        {
            error = "Die Zeit konnte nicht gelesen werden. Beispiele: `in 30m`, `in 2h`, `18:30`, `morgen 08:00`.";
            return false;
        }

        var lead = dueAt - now;
        if (lead < MinimumLead)
        {
            error = "Der Zeitpunkt liegt zu nah an jetzt (mindestens 1 Minute in der Zukunft).";
            return false;
        }

        if (lead > MaximumLead)
        {
            error = "Der Zeitpunkt liegt zu weit in der Zukunft (höchstens 1 Jahr).";
            return false;
        }

        return true;
    }

    /// <summary>Resolves one of the supported input forms into a local due time.</summary>
    /// <param name="text">The trimmed input.</param>
    /// <param name="now">The current local time.</param>
    /// <param name="dueAt">The resolved local due time.</param>
    /// <returns><see langword="true"/> when the form was recognised.</returns>
    private static bool TryResolve(string text, DateTime now, out DateTime dueAt)
    {
        dueAt = default;

        var relative = RelativePattern.Match(text);
        if (relative.Success)
        {
            var amount = int.Parse(relative.Groups["n"].Value, CultureInfo.InvariantCulture);
            var unit = relative.Groups["u"].Value.ToLowerInvariant();

            var span = unit switch
            {
                "s" or "sek" or "sekunden" => TimeSpan.FromSeconds(amount),
                "m" or "min" or "minuten" => TimeSpan.FromMinutes(amount),
                "h" or "std" or "stunde" or "stunden" => TimeSpan.FromHours(amount),
                "d" or "t" or "tag" or "tage" => TimeSpan.FromDays(amount),
                "w" or "woche" or "wochen" => TimeSpan.FromDays(amount * 7),
                _ => TimeSpan.Zero
            };

            if (span <= TimeSpan.Zero)
            {
                return false;
            }

            dueAt = now.Add(span);
            return true;
        }

        var clock = ClockTimePattern.Match(text);
        if (clock.Success)
        {
            var hour = int.Parse(clock.Groups["h"].Value, CultureInfo.InvariantCulture);
            var minute = int.Parse(clock.Groups["m"].Value, CultureInfo.InvariantCulture);
            if (hour > 23 || minute > 59)
            {
                return false;
            }

            var day = ResolveDay(clock.Groups["day"].Value, now, out var dayResolved);
            if (!dayResolved)
            {
                return false;
            }

            var candidate = day.Date.AddHours(hour).AddMinutes(minute);

            // A bare clock time that already passed means tomorrow.
            if (string.IsNullOrEmpty(clock.Groups["day"].Value) && candidate <= now)
            {
                candidate = candidate.AddDays(1);
            }

            dueAt = candidate;
            return true;
        }

        var dayOnly = DayOnlyPattern.Match(text);
        if (dayOnly.Success)
        {
            var day = ResolveDay(dayOnly.Groups["day"].Value, now, out var dayResolved);
            if (!dayResolved)
            {
                return false;
            }

            dueAt = day.Date.AddHours(8);
            return true;
        }

        return false;
    }

    /// <summary>Maps a day keyword to its calendar date.</summary>
    /// <param name="keyword">The matched keyword (may be empty).</param>
    /// <param name="now">The current local time.</param>
    /// <param name="resolved">Whether the keyword was understood.</param>
    /// <returns>The target date.</returns>
    private static DateTime ResolveDay(string keyword, DateTime now, out bool resolved)
    {
        resolved = true;
        var normalized = (keyword ?? string.Empty).Trim().ToLowerInvariant();

        return normalized switch
        {
            "" or "heute" => now.Date,
            "morgen" => now.Date.AddDays(1),
            "übermorgen" or "uebermorgen" => now.Date.AddDays(2),
            _ => MarkUnresolved(now, out resolved)
        };
    }

    /// <summary>Helper that flips <paramref name="resolved"/> to false and returns the input date.</summary>
    /// <param name="date">The date to return.</param>
    /// <param name="resolved">Set to <see langword="false"/>.</param>
    /// <returns>The unchanged date.</returns>
    private static DateTime MarkUnresolved(DateTime date, out bool resolved)
    {
        resolved = false;
        return date;
    }
}
