namespace WK7Bot.Core.Entities;

/// <summary>
/// Records that a DWD warning of a given identifier was already dispatched to a guild,
/// providing restart-safe duplicate suppression across process restarts.
/// </summary>
public class WarningDispatchLog
{
    /// <summary>
    /// Gets or sets the stable CAP warning identifier.
    /// </summary>
    public string WarningId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the Discord guild (server) the warning was posted to.
    /// </summary>
    public ulong GuildId { get; set; }

    /// <summary>
    /// Gets or sets the local time the warning was posted.
    /// </summary>
    public DateTime SentAt { get; set; }
}
