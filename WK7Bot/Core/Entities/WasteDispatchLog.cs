namespace WK7Bot.Core.Entities;

/// <summary>
/// Records that a waste notification of a given kind was already dispatched to a guild on a specific date,
/// providing restart-safe duplicate suppression across process restarts.
/// </summary>
public class WasteDispatchLog
{
    /// <summary>
    /// Gets or sets the dispatch kind (e.g., <c>daily_confirmation</c> or <c>weekly_overview</c>).
    /// </summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the Discord guild (server) the message was posted to.
    /// </summary>
    public ulong GuildId { get; set; }

    /// <summary>
    /// Gets or sets the calendar date the message was sent on (normalized to midnight).
    /// </summary>
    public DateTime SentOn { get; set; }
}

/// <summary>
/// Well-known dispatch kind constants used with <see cref="WasteDispatchLog"/>.
/// </summary>
public static class WasteDispatchKinds
{
    /// <summary>
    /// Kind for the daily "yesterday's collections" confirmation posted after 08:00.
    /// </summary>
    public const string DailyConfirmation = "daily_confirmation";

    /// <summary>
    /// Kind for the Monday morning weekly overview posted after 09:00.
    /// </summary>
    public const string WeeklyOverview = "weekly_overview";
}
