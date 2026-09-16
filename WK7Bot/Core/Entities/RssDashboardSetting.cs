namespace WK7Bot.Core.Entities;

/// <summary>
/// Stores the Discord message tracking details for the active RSS subscription dashboard.
/// </summary>
public class RssDashboardSetting
{
    /// <summary>
    /// Gets or sets the primary key for the setting entity.
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Gets or sets the Discord channel ID containing the active dashboard message.
    /// </summary>
    public ulong ChannelId { get; set; }

    /// <summary>
    /// Gets or sets the Discord message ID of the interactive dashboard.
    /// </summary>
    public ulong MessageId { get; set; }
}