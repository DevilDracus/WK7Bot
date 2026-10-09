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
    /// <summary>
    /// Head-room kept below <see cref="EmbedText.TotalLimit"/> so the embed is never rejected by Discord.
    /// </summary>
    private const int SafetyMargin = 150;

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
        var title = EmbedText.Truncate($"⚠️ {subject} für {locationLabel}", EmbedText.TitleLimit);
        var footer = ResolveFooter(options);

        // Whole-embed budget: title and footer are fixed, the headline description gets the next slice,
        // and every field draws from what remains so the embed stays inside Discord's total limit.
        int budget = EmbedText.TotalLimit - SafetyMargin - title.Length - footer.Length;

        var headline = EmbedText.Truncate(warning.Headline, Math.Clamp(budget, 0, EmbedText.DescriptionLimit));
        budget -= headline.Length;

        var onset = TakeField(ref budget, "🕐 Eintritt", FormatOnset(warning.Onset, postTime), inline: true);
        var expires = warning.Expires != DateTime.MaxValue
            ? TakeField(ref budget, "⏳ Gültig bis", FormatExpires(warning.Expires, postTime), inline: true)
            : null;
        var area = TakeField(ref budget, "📍 Gebiet", FormatArea(locationLabel, warning), inline: true);
        var severity = TakeField(ref budget, "⚠️ Schweregrad", FormatSeverity(warning.Severity), inline: true);
        var description = warning.Description.Length > 0
            ? TakeField(ref budget, "📝 Beschreibung", warning.Description, inline: false)
            : null;
        var instruction = warning.Instruction.Length > 0
            ? TakeField(ref budget, "✅ Empfehlung", warning.Instruction, inline: false)
            : null;

        var builder = new EmbedBuilder()
            .WithTitle(title)
            .WithColor(ResolveColor(warning.Severity))
            .WithTimestamp(postTime);

        if (headline.Length > 0)
        {
            builder.WithDescription(headline);
        }

        if (warning.Web.Length > 0)
        {
            builder.WithUrl(warning.Web);
        }

        AddFieldIfPresent(builder, onset);
        AddFieldIfPresent(builder, expires);
        AddFieldIfPresent(builder, area);
        AddFieldIfPresent(builder, severity);
        AddFieldIfPresent(builder, description);
        AddFieldIfPresent(builder, instruction);

        builder.WithFooter(footer);
        return builder.Build();
    }

    /// <summary>
    /// Draws one field from the remaining embed-wide budget: the value is first cut to Discord's per-field
    /// limit, then cut again when the budget has run low. Returns null when not even the field name fits.
    /// </summary>
    /// <param name="budget">The remaining character budget; reduced by the field's cost.</param>
    /// <param name="name">The field name.</param>
    /// <param name="value">The raw field value.</param>
    /// <param name="inline">Whether the field renders inline.</param>
    /// <returns>The field to add, or null when nothing fits.</returns>
    private static (string Name, string Value, bool Inline)? TakeField(
        ref int budget,
        string name,
        string value,
        bool inline)
    {
        if (budget <= name.Length)
        {
            return null;
        }

        var truncated = EmbedText.Truncate(value, EmbedText.FieldValueLimit);
        int cost = name.Length + truncated.Length;
        if (cost <= budget)
        {
            budget -= cost;
            return (name, truncated, inline);
        }

        int room = budget - name.Length;
        if (room <= 0)
        {
            return null;
        }

        budget = 0;
        return (name, EmbedText.Truncate(truncated, room), inline);
    }

    /// <summary>
    /// Adds a field produced by <see cref="TakeField"/> when one was returned.
    /// </summary>
    /// <param name="builder">The embed under construction.</param>
    /// <param name="field">The field to add, if any.</param>
    private static void AddFieldIfPresent(EmbedBuilder builder, (string Name, string Value, bool Inline)? field)
    {
        if (field is { } value)
        {
            builder.AddField(value.Name, value.Value, value.Inline);
        }
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
}
