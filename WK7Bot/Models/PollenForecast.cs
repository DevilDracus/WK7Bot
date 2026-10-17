namespace WK7Bot.Models;

/// <summary>
/// The DWD pollen hazard index for one region: eight pollen types with their level for today,
/// tomorrow and the day after tomorrow.
/// </summary>
/// <param name="RegionName">The DWD region name (for example "Sachsen").</param>
/// <param name="PartRegionName">The DWD part-region name (for example "Tiefland Sachsen").</param>
/// <param name="LastUpdate">The publication timestamp of the feed.</param>
/// <param name="Types">The per-pollen-type forecast.</param>
public sealed record PollenForecast(
    string RegionName,
    string PartRegionName,
    DateTimeOffset? LastUpdate,
    IReadOnlyList<PollenTypeForecast> Types);

/// <summary>One pollen type with its three-day hazard levels.</summary>
/// <param name="Name">The German pollen name (for example "Birke").</param>
/// <param name="Today">Today's level.</param>
/// <param name="Tomorrow">Tomorrow's level.</param>
/// <param name="DayAfterTomorrow">The level for the day after tomorrow.</param>
public sealed record PollenTypeForecast(
    string Name,
    PollenLevel Today,
    PollenLevel Tomorrow,
    PollenLevel DayAfterTomorrow);

/// <summary>
/// One DWD hazard level. The raw value keeps DWD's own notation ("0", "0-1", "2-3", "-1" for
/// missing data); <see cref="Value"/> is the numeric midpoint used for sorting and severity checks.
/// </summary>
/// <param name="Raw">The raw DWD level string.</param>
/// <param name="Value">The numeric midpoint (0.5 for "0-1", 2.5 for "2-3", -1 for missing).</param>
/// <param name="Description">The German description from the feed legend.</param>
public sealed record PollenLevel(string Raw, double Value, string Description)
{
    /// <summary>Gets a value indicating whether the level marks missing data.</summary>
    public bool IsMissing => Value < 0;

    /// <summary>Gets a value indicating whether any load is expected.</summary>
    public bool HasLoad => Value >= 1;

    /// <summary>Formats the level as <c>2 (mittlere Belastung)</c>.</summary>
    /// <returns>The formatted level.</returns>
    public string Display() => IsMissing ? "keine Daten" : $"{Raw} ({Description})";
}

/// <summary>The DWD UV index forecast for one city.</summary>
/// <param name="City">The DWD city name.</param>
/// <param name="Today">Today's UV index.</param>
/// <param name="Tomorrow">Tomorrow's UV index.</param>
/// <param name="DayAfterTomorrow">The UV index for the day after tomorrow.</param>
/// <param name="LastUpdate">The publication timestamp of the feed.</param>
public sealed record UvForecast(
    string City,
    int Today,
    int Tomorrow,
    int DayAfterTomorrow,
    DateTimeOffset? LastUpdate);
