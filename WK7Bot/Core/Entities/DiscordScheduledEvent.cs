namespace WK7Bot.Core.Entities;

/// <summary>
/// Tracks a Discord guild scheduled event created by the bot so it can be deleted after it ended.
/// </summary>
public class DiscordScheduledEvent
{
    /// <summary>Gets or sets the unique database identifier.</summary>
    public int Id { get; set; }

    /// <summary>Gets or sets the guild that owns the event.</summary>
    public ulong GuildId { get; set; }

    /// <summary>Gets or sets the Discord scheduled event identifier.</summary>
    public ulong EventId { get; set; }

    /// <summary>Gets or sets the feature kind that created the event (for example <c>spontan_treff</c>).</summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>Gets or sets the event name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the UTC end time after which the event should be deleted.</summary>
    public DateTime EndAt { get; set; }

    /// <summary>Gets or sets the UTC creation timestamp.</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
