namespace WK7Bot.Core.Utilities;

using System.Globalization;
using System.Text.Json;
using WK7Bot.Models;

/// <summary>
/// Parses the DWD open-data pollen (<c>s31fg.json</c>) and UV index (<c>uvi.json</c>) feeds. Both are
/// plain JSON documents on <c>opendata.dwd.de</c> with no API key; the parsers are pure so they can be
/// unit-tested against captured payloads.
/// </summary>
public static class PollenFeedParser
{
    /// <summary>The pollen types the DWD publishes, in a stable display order.</summary>
    public static readonly string[] PollenTypes =
    {
        "Hasel", "Erle", "Esche", "Birke", "Graeser", "Roggen", "Beifuss", "Ambrosia"
    };

    /// <summary>
    /// Parses the pollen feed and extracts the requested region.
    /// </summary>
    /// <param name="json">The raw feed payload.</param>
    /// <param name="regionName">The DWD region name to match (case-insensitive substring match).</param>
    /// <param name="partRegionName">The DWD part-region name to prefer (case-insensitive substring match).</param>
    /// <returns>The forecast, or <see langword="null"/> when the region is not present.</returns>
    public static PollenForecast? Parse(string json, string regionName, string partRegionName)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        var legend = ParseLegend(root);
        var lastUpdate = ParseTimestamp(root, "last_update");

        if (!root.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        JsonElement? best = null;
        var bestScore = 0;

        foreach (var entry in content.EnumerateArray())
        {
            var region = entry.TryGetProperty("region_name", out var r) ? r.GetString() ?? string.Empty : string.Empty;
            var part = entry.TryGetProperty("partregion_name", out var p) ? p.GetString() ?? string.Empty : string.Empty;

            // An exact match must always beat a substring match: "Tiefland Sachsen" is a substring of
            // "Tiefland Sachsen-Anhalt", so counting only substrings would pick the neighbouring state.
            var score = 0;
            if (!string.IsNullOrWhiteSpace(regionName))
            {
                score += region.Equals(regionName, StringComparison.OrdinalIgnoreCase) ? 2
                    : region.Contains(regionName, StringComparison.OrdinalIgnoreCase) ? 1
                    : 0;
            }

            if (!string.IsNullOrWhiteSpace(partRegionName))
            {
                score += part.Equals(partRegionName, StringComparison.OrdinalIgnoreCase) ? 4
                    : part.Contains(partRegionName, StringComparison.OrdinalIgnoreCase) ? 2
                    : 0;
            }

            if (score > bestScore)
            {
                bestScore = score;
                best = entry;
            }
        }

        if (best is null || !best.Value.TryGetProperty("Pollen", out var pollen))
        {
            return null;
        }

        var types = new List<PollenTypeForecast>();
        foreach (var typeName in PollenTypes)
        {
            if (!pollen.TryGetProperty(typeName, out var levels))
            {
                continue;
            }

            types.Add(new PollenTypeForecast(
                DisplayName(typeName),
                ParseLevel(levels, "today", legend),
                ParseLevel(levels, "tomorrow", legend),
                ParseLevel(levels, "dayafter_to", legend)));
        }

        var regionLabel = best.Value.TryGetProperty("region_name", out var rn) ? rn.GetString() ?? regionName : regionName;
        var partLabel = best.Value.TryGetProperty("partregion_name", out var pn) ? pn.GetString() ?? partRegionName : partRegionName;

        return new PollenForecast(regionLabel, partLabel, lastUpdate, types);
    }

    /// <summary>
    /// Parses the UV index feed and extracts one city.
    /// </summary>
    /// <param name="json">The raw feed payload.</param>
    /// <param name="city">The city name to match (case-insensitive exact match, then substring).</param>
    /// <returns>The forecast, or <see langword="null"/> when the city is not present.</returns>
    public static UvForecast? ParseUv(string json, string city)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var lastUpdate = ParseTimestamp(root, "last_update");

        if (!root.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var entry in content.EnumerateArray())
        {
            var name = entry.TryGetProperty("city", out var c) ? c.GetString() ?? string.Empty : string.Empty;
            if (!name.Equals(city, StringComparison.OrdinalIgnoreCase)
                && !name.Contains(city, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!entry.TryGetProperty("forecast", out var forecast))
            {
                continue;
            }

            return new UvForecast(
                name,
                ReadInt(forecast, "today"),
                ReadInt(forecast, "tomorrow"),
                ReadInt(forecast, "dayafter_to"),
                lastUpdate);
        }

        return null;
    }

    /// <summary>Reads the level legend (<c>id1</c> → <c>id1_desc</c>) from the feed.</summary>
    /// <param name="root">The feed root element.</param>
    /// <returns>The legend keyed by the raw level string.</returns>
    private static Dictionary<string, string> ParseLegend(JsonElement root)
    {
        var legend = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!root.TryGetProperty("legend", out var legendElement) || legendElement.ValueKind != JsonValueKind.Object)
        {
            return legend;
        }

        for (var i = 1; i <= 7; i++)
        {
            if (legendElement.TryGetProperty($"id{i}", out var key) && legendElement.TryGetProperty($"id{i}_desc", out var description))
            {
                var raw = key.GetString();
                if (!string.IsNullOrWhiteSpace(raw))
                {
                    legend[raw] = description.GetString() ?? string.Empty;
                }
            }
        }

        return legend;
    }

    /// <summary>Reads one pollen level and resolves its legend description.</summary>
    /// <param name="levels">The pollen type element.</param>
    /// <param name="property">The property name (<c>today</c>, <c>tomorrow</c>, <c>dayafter_to</c>).</param>
    /// <param name="legend">The level legend.</param>
    /// <returns>The parsed level.</returns>
    private static PollenLevel ParseLevel(JsonElement levels, string property, Dictionary<string, string> legend)
    {
        var raw = levels.TryGetProperty(property, out var value) ? value.GetString() ?? string.Empty : string.Empty;
        raw = raw.Trim();

        if (raw.Length == 0)
        {
            return new PollenLevel("-1", -1, "keine Daten");
        }

        var description = legend.TryGetValue(raw, out var text) ? text : raw;
        return new PollenLevel(raw, ToNumeric(raw), description);
    }

    /// <summary>Converts DWD's level notation into a numeric midpoint.</summary>
    /// <param name="raw">The raw level string.</param>
    /// <returns>The numeric value, or -1 for missing data.</returns>
    private static double ToNumeric(string raw)
    {
        if (raw.StartsWith('-'))
        {
            return -1;
        }

        var parts = raw.Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var values = new List<double>();
        foreach (var part in parts)
        {
            if (double.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
            {
                values.Add(parsed);
            }
        }

        return values.Count == 0 ? -1 : values.Average();
    }

    /// <summary>Reads an integer property, defaulting to 0.</summary>
    /// <param name="element">The parent element.</param>
    /// <param name="property">The property name.</param>
    /// <returns>The parsed integer.</returns>
    private static int ReadInt(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value))
        {
            return 0;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetInt32(out var number) => number,
            JsonValueKind.String when int.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => 0
        };
    }

    /// <summary>Parses a timestamp property, tolerating both ISO and German date formats.</summary>
    /// <param name="root">The feed root element.</param>
    /// <param name="property">The property name.</param>
    /// <returns>The timestamp, or <see langword="null"/>.</returns>
    private static DateTimeOffset? ParseTimestamp(JsonElement root, string property)
    {
        if (!root.TryGetProperty(property, out var value))
        {
            return null;
        }

        var text = value.GetString();
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        if (DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var parsed))
        {
            return parsed;
        }

        // The pollen feed uses "2026-10-16 11:00 Uhr"; strip the suffix and retry.
        var cleaned = text.Replace("Uhr", string.Empty, StringComparison.OrdinalIgnoreCase).Trim();
        return DateTimeOffset.TryParse(cleaned, CultureInfo.GetCultureInfo("de-DE"), DateTimeStyles.AssumeLocal, out var german)
            ? german
            : null;
    }

    /// <summary>Maps the DWD ASCII pollen key to its display name.</summary>
    /// <param name="key">The DWD key.</param>
    /// <returns>The display name.</returns>
    private static string DisplayName(string key) => key switch
    {
        "Graeser" => "Gräser",
        "Beifuss" => "Beifuß",
        _ => key
    };
}
