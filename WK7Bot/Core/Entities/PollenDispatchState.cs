namespace WK7Bot.Core.Entities;

/// <summary>
/// Persists the last pollen forecast that was communicated to a guild, so the 3-day cadence and
/// DWD revisions can be evaluated even after a bot restart.
/// </summary>
public class PollenDispatchState
{
    /// <summary>
    /// Gets or sets the Discord guild (server) the forecast was posted to.
    /// </summary>
    public ulong GuildId { get; set; }

    /// <summary>
    /// Gets or sets the calendar date the forecast was posted on (normalized to midnight). The
    /// stored forecast is aligned to this date (today/tomorrow/day-after).
    /// </summary>
    public DateTime LastPostedDate { get; set; }

    /// <summary>
    /// Gets or sets the serialized <see cref="WK7Bot.Models.PollenForecast"/> that was posted,
    /// used for revision detection against the current feed.
    /// </summary>
    public string ForecastJson { get; set; } = string.Empty;
}