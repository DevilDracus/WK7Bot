namespace WK7Bot.Core.Utilities;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Discord;
using WK7Bot.Models;
using WK7Bot.Options;

/// <summary>
/// Builds the German Discord embed for a local DWD rain/storm warning: headline, onset countdown,
/// validity window, area, severity and the official description/instruction within Discord's limits.
/// </summary>
public static class DwdWarningMessageBuilder
{
    private const int TitleLimit = 256;
    private const int DescriptionLimit = 4096;
    private const int FieldNameLimit = 256;
    private const int FieldValueLimit = 1024;

    private static readonly CultureInfo GermanCulture = new("de-DE");

    /// <summary>
    /// Renders the warning embed.
    /// </summary>
    /// <param name="warning">The warning to render.</param>
    /// <param name="postTime">The local time the message is posted (anchors the countdown).</param>
    /// <param name="options">The postal-code-specific warning options (area names, PLZ).</param>
    /// <returns>The embed to send.</returns>
    public static Embed Build(CapWarning warning, DateTime postTime, DwdWarningOptions options)
    {
        ArgumentNullException.ThrowIfNull(warning);
        ArgumentNullException.ThrowIfNull(options);

        var locationLabel = ResolveLocationLabel(warning, options);
        var subject = warning.Event.Length > 0 ? warning.Event : warning.Headline;

        var builder = new EmbedBuilder()
            .WithTitle(Truncate($"⚠️ {subject} für {locationLabel}", TitleLimit))
            .WithColor(ResolveColor(warning.Severity))
            .WithTimestamp(postTime);

        if (warning.Headline.Length > 0)
        {
            builder.WithDescription(Truncate(warning.Headline, DescriptionLimit));
        }

        if (warning.Web.Length > 0)
        {
            builder.WithUrl(warning.Web);
        }

        builder.AddField(
            Truncate("🕐 Eintritt", FieldNameLimit),
            Truncate(FormatOnset(warning.Onset, postTime), FieldValueLimit),
            true);

        if (warning.Expires != DateTime.MaxValue)
        {
            builder.AddField(
                Truncate("⏳ Gültig bis", FieldNameLimit),
                Truncate(FormatExpires(warning.Expires, postTime), FieldValueLimit),
                true);
        }

        builder.AddField(
            Truncate("📍 Gebiet", FieldNameLimit),
            Truncate(FormatArea(locationLabel, warning), FieldValueLimit),
            true);

        builder.AddField(
            Truncate("⚠️ Schweregrad", FieldNameLimit),
            Truncate(FormatSeverity(warning.Severity), FieldValueLimit),
            true);

        if (warning.Description.Length > 0)
        {
            builder.AddField(
                Truncate("📝 Beschreibung", FieldNameLimit),
                Truncate(warning.Description, FieldValueLimit),
                false);
        }

        if (warning.Instruction.Length > 0)
        {
            builder.AddField(
                Truncate("✅ Empfehlung", FieldNameLimit),
                Truncate(warning.Instruction, FieldValueLimit),
                false);
        }

        builder.WithFooter(ResolveFooter(options));
        return builder.Build();
    }

    /// <summary>
    /// Picks the location label shown in the title: a configured area name the warning covers,
    /// else the first named area of the warning, else the configured postal code.
    /// </summary>
    /// <param name="warning">The warning to label.</param>
    /// <param name="options">The warning options.</param>
    /// <returns>The location label.</returns>
    private static string ResolveLocationLabel(CapWarning warning, DwdWarningOptions options)
    {
        foreach (var areaName in options.AreaNames)
        {
            var trimmed = areaName?.Trim();
            if (string.IsNullOrEmpty(trimmed))
            {
                continue;
            }

            if (warning.AreaDescriptions.Any(area =>
                    string.Equals(area.Trim(), trimmed, StringComparison.OrdinalIgnoreCase)))
            {
                return trimmed;
            }
        }

        var namedArea = warning.AreaDescriptions.FirstOrDefault(area =>
            area.Trim().Length > 0 &&
            !string.Equals(area.Trim(), "polygonal event area", StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(namedArea))
        {
            return namedArea.Trim();
        }

        return options.HasPostalCode() ? $"PLZ {options.PostalCode}" : "die Umgebung";
    }

    /// <summary>
    /// Formats the area field, appending the number of additional covered places when the warning
    /// spans more than the displayed label.
    /// </summary>
    /// <param name="locationLabel">The label shown in the title.</param>
    /// <param name="warning">The warning being rendered.</param>
    /// <returns>The formatted area text.</returns>
    private static string FormatArea(string locationLabel, CapWarning warning)
    {
        var additional = warning.AreaDescriptions.Count(area =>
            !string.Equals(area.Trim(), locationLabel, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(area.Trim(), "polygonal event area", StringComparison.OrdinalIgnoreCase));

        return additional > 0 ? $"{locationLabel} (+{additional} weitere Orte)" : locationLabel;
    }

    /// <summary>
    /// Formats the onset line with a countdown for future warnings and a "running since" marker
    /// for ones that already started.
    /// </summary>
    /// <param name="onset">The local onset time.</param>
    /// <param name="postTime">The local posting time.</param>
    /// <returns>The formatted onset text.</returns>
    private static string FormatOnset(DateTime onset, DateTime postTime)
    {
        if (onset > postTime)
        {
            return $"ab {onset.ToString("HH:mm", GermanCulture)} Uhr (in {FormatDuration(onset - postTime)})";
        }

        return $"seit {onset.ToString("HH:mm", GermanCulture)} Uhr";
    }

    /// <summary>
    /// Formats the expiry time, including the date when it falls on another day than the post.
    /// </summary>
    /// <param name="expires">The local expiry time.</param>
    /// <param name="postTime">The local posting time.</param>
    /// <returns>The formatted expiry text.</returns>
    private static string FormatExpires(DateTime expires, DateTime postTime)
        => expires.Date == postTime.Date
            ? $"bis {expires.ToString("HH:mm", GermanCulture)} Uhr"
            : $"bis {expires.ToString("dd.MM., HH:mm", GermanCulture)} Uhr";

    /// <summary>
    /// Formats a time span as a compact German duration (<c>12 Min.</c>, <c>2 Std. 5 Min.</c>).
    /// </summary>
    /// <param name="duration">The non-negative duration to format.</param>
    /// <returns>The formatted duration.</returns>
    private static string FormatDuration(TimeSpan duration)
    {
        var minutes = (int)Math.Round(Math.Abs(duration.TotalMinutes));

        if (minutes < 60)
        {
            return $"{minutes} Min.";
        }

        var hours = minutes / 60;
        var rest = minutes % 60;
        return rest == 0 ? $"{hours} Std." : $"{hours} Std. {rest} Min.";
    }

    /// <summary>
    /// Translates the CAP severity into a German label with a coloured marker.
    /// </summary>
    /// <param name="severity">The raw CAP severity.</param>
    /// <returns>The formatted severity.</returns>
    private static string FormatSeverity(string severity)
        => severity.ToUpperInvariant() switch
        {
            "EXTREME" => "🔴 Extrem",
            "SEVERE" => "🟠 Schwer",
            "MODERATE" => "🟡 Mittel",
            "MINOR" => "🔵 Gering",
            _ => severity.Length > 0 ? severity : "unbekannt"
        };

    /// <summary>
    /// Maps the CAP severity to the embed colour.
    /// </summary>
    /// <param name="severity">The raw CAP severity.</param>
    /// <returns>The embed colour.</returns>
    private static Color ResolveColor(string severity)
        => severity.ToUpperInvariant() switch
        {
            "EXTREME" => Color.Red,
            "SEVERE" => Color.Orange,
            "MODERATE" => Color.Gold,
            _ => new Color(88, 101, 242)
        };

    /// <summary>
    /// Builds the embed footer with the data source and the monitored postal code.
    /// </summary>
    /// <param name="options">The warning options.</param>
    /// <returns>The footer text.</returns>
    private static string ResolveFooter(DwdWarningOptions options)
        => options.HasPostalCode()
            ? $"Deutscher Wetterdienst (DWD) • PLZ {options.PostalCode}"
            : "Deutscher Wetterdienst (DWD)";

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
