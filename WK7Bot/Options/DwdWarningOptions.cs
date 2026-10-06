namespace WK7Bot.Options;

using System.Collections.Generic;
using Microsoft.Extensions.Configuration;

/// <summary>
/// Configuration for the local DWD rain and storm warning feature: which postal code / coordinates
/// are monitored, how far ahead warnings trigger, and which events are considered relevant.
/// All postal-code-specific values live here (config only) so the feature is portable.
/// </summary>
public class DwdWarningOptions
{
    /// <summary>
    /// Gets or sets the monitored postal code (e.g. <c>04205</c>). Used for identification and
    /// display only; when empty the feature stays idle.
    /// </summary>
    [ConfigurationKeyName("postal_code")]
    public string PostalCode { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the latitude of the monitored location in WGS84 degrees.
    /// Point-in-polygon matching is skipped while both coordinates are unset.
    /// </summary>
    [ConfigurationKeyName("latitude")]
    public double Latitude { get; set; }

    /// <summary>
    /// Gets or sets the longitude of the monitored location in WGS84 degrees.
    /// </summary>
    [ConfigurationKeyName("longitude")]
    public double Longitude { get; set; }

    /// <summary>
    /// Gets or sets how many minutes before a warning's onset it is posted to Discord.
    /// </summary>
    [ConfigurationKeyName("lead_minutes")]
    public int LeadMinutes { get; set; } = 30;

    /// <summary>
    /// Gets or sets the area names (e.g. <c>Stadt Leipzig</c>) that match a warning when the
    /// warning carries no usable polygon. Config provides the postal-code-specific values.
    /// </summary>
    [ConfigurationKeyName("area_names")]
    public List<string> AreaNames { get; set; } = new();

    /// <summary>
    /// Gets or sets the event tokens (German DWD event names or English CAP group codes, e.g.
    /// <c>STARKREGEN</c>, <c>HAGEL</c>, <c>HEAVY_RAIN</c>) a warning must mention to be posted.
    /// Left empty, <see cref="EffectiveEvents"/> falls back to <see cref="DefaultEvents"/>; when
    /// configured, the configured list replaces the built-in defaults.
    /// </summary>
    [ConfigurationKeyName("events")]
    public List<string> Events { get; set; } = new();

    /// <summary>
    /// Built-in event tokens used when no <c>events</c> list is configured: heavy rain, hail,
    /// thunderstorms, storms and wind gusts (German event names plus English group codes).
    /// </summary>
    public static readonly IReadOnlyList<string> DefaultEvents = new[]
    {
        "STARKREGEN",
        "HAGEL",
        "GEWITTER",
        "STURM",
        "BÖEN",
        "BOEEN",
        "HEAVY_RAIN",
        "HAIL",
        "THUNDER",
        "STORM"
    };

    /// <summary>
    /// Returns the configured event tokens, or the built-in defaults when none are configured.
    /// </summary>
    /// <returns>The event tokens to match warnings against.</returns>
    public IReadOnlyList<string> EffectiveEvents()
        => Events.Count > 0 ? Events : DefaultEvents;

    /// <summary>
    /// Determines whether a monitored location is configured at all.
    /// </summary>
    /// <returns><see langword="true"/> when a postal code is set.</returns>
    public bool HasPostalCode()
        => !string.IsNullOrWhiteSpace(PostalCode);

    /// <summary>
    /// Determines whether concrete coordinates are configured for polygon matching.
    /// </summary>
    /// <returns><see langword="true"/> when latitude and longitude are both non-zero.</returns>
    public bool HasCoordinates()
        => Latitude != 0 || Longitude != 0;
}
