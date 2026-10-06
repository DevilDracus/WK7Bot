namespace WK7Bot.Core.Utilities;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using WK7Bot.Models;

/// <summary>
/// Pure parsing and matching logic for the DWD CAP (Common Alerting Protocol) warning feed:
/// XML → <see cref="CapWarning"/> plus the polygon and event/location matching rules used to decide
/// whether a warning applies to the configured location.
/// </summary>
public static class CapWarningParser
{
    private static readonly XNamespace CapNamespace = "urn:oasis:names:tc:emergency:cap:1.2";

    /// <summary>
    /// Parses every <c>info</c> element of a CAP alert document into a warning.
    /// </summary>
    /// <param name="xmlStream">The stream carrying the UTF-8 CAP XML document.</param>
    /// <returns>One warning per <c>info</c> element (a single one for DWD alerts).</returns>
    /// <exception cref="System.Xml.XmlException">The document is not well-formed XML.</exception>
    public static IReadOnlyList<CapWarning> ParseAlert(Stream xmlStream)
    {
        ArgumentNullException.ThrowIfNull(xmlStream);

        var document = XDocument.Load(xmlStream);
        return ParseAlertDocument(document);
    }

    /// <summary>
    /// Parses every <c>info</c> element of a CAP alert document given as a string.
    /// </summary>
    /// <param name="xml">The CAP XML document.</param>
    /// <returns>One warning per <c>info</c> element.</returns>
    /// <exception cref="System.Xml.XmlException">The document is not well-formed XML.</exception>
    public static IReadOnlyList<CapWarning> ParseAlert(string xml)
    {
        ArgumentNullException.ThrowIfNull(xml);

        var document = XDocument.Parse(xml);
        return ParseAlertDocument(document);
    }

    /// <summary>
    /// Parses a CAP polygon coordinate list (<c>lat,lon</c> pairs separated by whitespace) into points.
    /// Malformed tokens are skipped.
    /// </summary>
    /// <param name="text">The raw polygon text.</param>
    /// <returns>The parsed points in feed order (latitude first).</returns>
    public static IReadOnlyList<GeoPoint> ParsePolygon(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Array.Empty<GeoPoint>();
        }

        var points = new List<GeoPoint>();

        foreach (var token in text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = token.Split(',');
            if (parts.Length != 2)
            {
                continue;
            }

            if (!double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var latitude) ||
                !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var longitude))
            {
                continue;
            }

            points.Add(new GeoPoint(latitude, longitude));
        }

        return points;
    }

    /// <summary>
    /// Determines whether a point lies inside a polygon using the ray-casting algorithm.
    /// </summary>
    /// <param name="polygon">The polygon points (latitude/longitude pairs).</param>
    /// <param name="latitude">The point latitude in WGS84 degrees.</param>
    /// <param name="longitude">The point longitude in WGS84 degrees.</param>
    /// <returns><see langword="true"/> when the point is inside the polygon.</returns>
    public static bool PointInPolygon(IReadOnlyList<GeoPoint> polygon, double latitude, double longitude)
    {
        if (polygon is null || polygon.Count < 3)
        {
            return false;
        }

        var inside = false;

        for (var i = 0; i < polygon.Count; i++)
        {
            var current = polygon[i];
            var previous = polygon[(i + polygon.Count - 1) % polygon.Count];

            var crosses = (current.Latitude > latitude) != (previous.Latitude > latitude);
            if (!crosses)
            {
                continue;
            }

            var intersectionLongitude =
                (previous.Longitude - current.Longitude) * (latitude - current.Latitude) /
                (previous.Latitude - current.Latitude) + current.Longitude;

            if (longitude < intersectionLongitude)
            {
                inside = !inside;
            }
        }

        return inside;
    }

    /// <summary>
    /// Determines whether a warning covers the given point. The include polygons decide when the
    /// warning carries any (an explicit <c>EXCLUDE_POLYGON</c> carve-out vetoes the match); the
    /// configured area names only apply when the warning has no include polygon at all.
    /// </summary>
    /// <param name="warning">The warning to test.</param>
    /// <param name="latitude">The monitored latitude in WGS84 degrees.</param>
    /// <param name="longitude">The monitored longitude in WGS84 degrees.</param>
    /// <param name="areaNames">Exact area descriptions used as a fallback (may be empty).</param>
    /// <returns><see langword="true"/> when the warning covers the point.</returns>
    public static bool MatchesLocation(
        CapWarning warning,
        double latitude,
        double longitude,
        IReadOnlyList<string> areaNames)
    {
        ArgumentNullException.ThrowIfNull(warning);

        if (warning.Polygons.Count > 0)
        {
            foreach (var excludePolygon in warning.ExcludePolygons)
            {
                if (PointInPolygon(excludePolygon, latitude, longitude))
                {
                    return false;
                }
            }

            foreach (var polygon in warning.Polygons)
            {
                if (PointInPolygon(polygon, latitude, longitude))
                {
                    return true;
                }
            }

            return false;
        }

        return MatchesAreaName(warning, areaNames);
    }

    /// <summary>
    /// Determines whether a warning mentions one of the configured events. Both sides are compared
    /// case-insensitively after normalising umlauts, spaces, hyphens and underscores, so German
    /// event names (<c>BÖEN</c>) and English group codes (<c>HEAVY_RAIN</c>) match alike.
    /// </summary>
    /// <param name="warning">The warning to test.</param>
    /// <param name="events">The configured event tokens.</param>
    /// <returns><see langword="true"/> when at least one token matches the event or its group code.</returns>
    public static bool MatchesEvent(CapWarning warning, IReadOnlyList<string> events)
    {
        ArgumentNullException.ThrowIfNull(warning);

        if (events is null || events.Count == 0)
        {
            return false;
        }

        var haystack = Normalize($"{warning.Event} {warning.EventGroup}");

        foreach (var evt in events)
        {
            var token = Normalize(evt);
            if (token.Length == 0)
            {
                continue;
            }

            if (haystack.Contains(token, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Parses a loaded CAP document into one warning per <c>info</c> element.
    /// </summary>
    /// <param name="document">The parsed CAP document.</param>
    /// <returns>The parsed warnings.</returns>
    private static IReadOnlyList<CapWarning> ParseAlertDocument(XDocument document)
    {
        var root = document.Root;
        if (root is null)
        {
            return Array.Empty<CapWarning>();
        }

        var identifier = Value(root, "identifier");
        var sent = Value(root, "sent");
        var infoElements = root.Elements(CapNamespace + "info").ToList();
        var warnings = new List<CapWarning>(infoElements.Count);

        for (var index = 0; index < infoElements.Count; index++)
        {
            var info = infoElements[index];
            var warningIdentifier = infoElements.Count == 1
                ? identifier
                : $"{identifier}#{index + 1}";

            warnings.Add(ParseInfo(info, warningIdentifier, sent));
        }

        return warnings;
    }

    /// <summary>
    /// Maps a single CAP <c>info</c> element to a <see cref="CapWarning"/>.
    /// </summary>
    /// <param name="info">The <c>info</c> element.</param>
    /// <param name="identifier">The alert identifier (suffixed per info element).</param>
    /// <param name="sent">The alert send time as raw CAP text.</param>
    /// <returns>The parsed warning.</returns>
    private static CapWarning ParseInfo(XElement info, string identifier, string sent)
    {
        var areaDescriptions = new List<string>();
        var polygons = new List<IReadOnlyList<GeoPoint>>();
        var excludePolygons = new List<IReadOnlyList<GeoPoint>>();

        foreach (var area in info.Elements(CapNamespace + "area"))
        {
            var areaDesc = Value(area, "areaDesc");
            if (areaDesc.Length > 0)
            {
                areaDescriptions.Add(areaDesc);
            }

            var polygonText = area.Element(CapNamespace + "polygon")?.Value;
            var polygon = ParsePolygon(polygonText ?? string.Empty);
            if (polygon.Count >= 3)
            {
                polygons.Add(polygon);
            }

            foreach (var geocode in area.Elements(CapNamespace + "geocode"))
            {
                if (!string.Equals(Value(geocode, "valueName"), "EXCLUDE_POLYGON", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var exclude = ParsePolygon(Value(geocode, "value"));
                if (exclude.Count >= 3)
                {
                    excludePolygons.Add(exclude);
                }
            }
        }

        var effective = Value(info, "effective");
        var onset = ParseTime(Value(info, "onset"), ParseTime(effective, ParseTime(sent, DateTime.MinValue)));

        return new CapWarning
        {
            Identifier = identifier,
            Event = Value(info, "event"),
            EventGroup = ReadEventGroup(info),
            Severity = Value(info, "severity"),
            Headline = Value(info, "headline"),
            Description = Value(info, "description"),
            Instruction = Value(info, "instruction"),
            Web = Value(info, "web"),
            Onset = onset,
            Expires = ParseTime(Value(info, "expires"), DateTime.MaxValue),
            AreaDescriptions = areaDescriptions,
            Polygons = polygons,
            ExcludePolygons = excludePolygons
        };
    }

    /// <summary>
    /// Reads the English <c>GROUP</c> event code of an info element.
    /// </summary>
    /// <param name="info">The <c>info</c> element.</param>
    /// <returns>The group code, or an empty string when none is present.</returns>
    private static string ReadEventGroup(XElement info)
    {
        foreach (var eventCode in info.Elements(CapNamespace + "eventCode"))
        {
            if (string.Equals(Value(eventCode, "valueName"), "GROUP", StringComparison.OrdinalIgnoreCase))
            {
                return Value(eventCode, "value");
            }
        }

        return string.Empty;
    }

    /// <summary>
    /// Determines whether a warning's area descriptions include one of the configured names.
    /// </summary>
    /// <param name="warning">The warning to test.</param>
    /// <param name="areaNames">The configured area names.</param>
    /// <returns><see langword="true"/> when an exact (case-insensitive) match exists.</returns>
    private static bool MatchesAreaName(CapWarning warning, IReadOnlyList<string> areaNames)
    {
        if (areaNames is null || areaNames.Count == 0)
        {
            return false;
        }

        foreach (var areaDesc in warning.AreaDescriptions)
        {
            var trimmed = areaDesc.Trim();
            if (trimmed.Length == 0)
            {
                continue;
            }

            foreach (var areaName in areaNames)
            {
                if (string.Equals(trimmed, areaName?.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Reads the direct child text of the given CAP element.
    /// </summary>
    /// <param name="parent">The parent element.</param>
    /// <param name="name">The local element name.</param>
    /// <returns>The trimmed value, or an empty string when the element is missing.</returns>
    private static string Value(XElement parent, string name)
        => parent.Element(CapNamespace + name)?.Value.Trim() ?? string.Empty;

    /// <summary>
    /// Parses a CAP ISO-8601 timestamp (with offset) to local time.
    /// </summary>
    /// <param name="value">The raw timestamp text.</param>
    /// <param name="fallback">The value to use when the text is missing or unparsable.</param>
    /// <returns>The local time, or <paramref name="fallback"/>.</returns>
    private static DateTime ParseTime(string value, DateTime fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        return DateTimeOffset.TryParse(
                   value,
                   CultureInfo.InvariantCulture,
                   DateTimeStyles.AssumeUniversal,
                   out var timestamp)
            ? timestamp.LocalDateTime
            : fallback;
    }

    /// <summary>
    /// Normalises an event token for substring matching: upper-cased, umlauts expanded
    /// (<c>BÖEN</c> → <c>BOEEN</c>) and separators removed (<c>HEAVY_RAIN</c> → <c>HEAVYRAIN</c>).
    /// </summary>
    /// <param name="value">The raw text.</param>
    /// <returns>The normalised text.</returns>
    private static string Normalize(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value
            .ToUpperInvariant()
            .Replace("Ä", "AE", StringComparison.Ordinal)
            .Replace("Ö", "OE", StringComparison.Ordinal)
            .Replace("Ü", "UE", StringComparison.Ordinal)
            .Replace("ẞ", "SS", StringComparison.Ordinal)
            .Replace("ß", "SS", StringComparison.Ordinal)
            .Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal);
    }
}
