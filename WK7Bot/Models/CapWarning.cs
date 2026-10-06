namespace WK7Bot.Models;

using System.Collections.Generic;

/// <summary>
/// A geographic coordinate of a CAP warning polygon. DWD polygons list the latitude first
/// (<c>lat,lon</c> pairs), which is preserved here.
/// </summary>
/// <param name="Latitude">The WGS84 latitude in degrees.</param>
/// <param name="Longitude">The WGS84 longitude in degrees.</param>
public readonly record struct GeoPoint(double Latitude, double Longitude);

/// <summary>
/// A single weather warning parsed from the DWD CAP (Common Alerting Protocol) feed, reduced to the
/// fields needed for matching a configured location and rendering the Discord embed.
/// </summary>
public class CapWarning
{
    /// <summary>
    /// Gets or sets the stable CAP identifier used as the duplicate-suppression key.
    /// </summary>
    public string Identifier { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the German DWD event name (e.g. <c>STARKREGEN</c>, <c>NEBEL</c>).
    /// </summary>
    public string Event { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the English CAP group code (e.g. <c>HEAVY RAIN</c>) when the feed carries one.
    /// </summary>
    public string EventGroup { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the CAP severity (<c>Minor</c>, <c>Moderate</c>, <c>Severe</c>, <c>Extreme</c>).
    /// </summary>
    public string Severity { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the official headline (e.g. <c>Amtliche WARNUNG vor STARKREGEN</c>).
    /// </summary>
    public string Headline { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the human-readable description of the warning.
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the recommended course of action, if the feed provides one.
    /// </summary>
    public string Instruction { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the source URL of the warning, if provided.
    /// </summary>
    public string Web { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the local onset time of the warning (<c>onset</c>, falling back to
    /// <c>effective</c> and <c>sent</c>), converted to the local time zone.
    /// </summary>
    public DateTime Onset { get; set; }

    /// <summary>
    /// Gets or sets the local expiry time of the warning; <see cref="DateTime.MaxValue"/> when the
    /// feed does not provide one.
    /// </summary>
    public DateTime Expires { get; set; } = DateTime.MaxValue;

    /// <summary>
    /// Gets or sets the area descriptions (e.g. <c>Stadt Leipzig</c>) the warning covers.
    /// </summary>
    public IReadOnlyList<string> AreaDescriptions { get; set; } = new List<string>();

    /// <summary>
    /// Gets or sets the include polygons of the warning.
    /// </summary>
    public IReadOnlyList<IReadOnlyList<GeoPoint>> Polygons { get; set; } = new List<IReadOnlyList<GeoPoint>>();

    /// <summary>
    /// Gets or sets the carved-out exclusion polygons (<c>EXCLUDE_POLYGON</c> geocodes) that punch
    /// holes into the covered area.
    /// </summary>
    public IReadOnlyList<IReadOnlyList<GeoPoint>> ExcludePolygons { get; set; } = new List<IReadOnlyList<GeoPoint>>();
}
