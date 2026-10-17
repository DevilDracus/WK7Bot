namespace WK7Bot.Core.Entities;

/// <summary>
/// A personal reminder created with <c>/remind</c>. It is delivered once by
/// <c>ReminderBackgroundService</c> as a direct message or, when a channel was requested, as a
/// channel post that mentions the creator.
/// </summary>
public class Reminder
{
    /// <summary>Gets or sets the unique database identifier shown by <c>/reminders</c>.</summary>
    public int Id { get; set; }

    /// <summary>Gets or sets the Discord user the reminder belongs to.</summary>
    public ulong DiscordUserId { get; set; }

    /// <summary>Gets or sets the guild the reminder was created in (0 when created outside a guild).</summary>
    public ulong GuildId { get; set; }

    /// <summary>Gets or sets the channel to post in; 0 means the reminder is delivered as a DM.</summary>
    public ulong ChannelId { get; set; }

    /// <summary>Gets or sets the reminder text.</summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>Gets or sets the UTC due time.</summary>
    public DateTime DueAt { get; set; }

    /// <summary>Gets or sets the UTC creation timestamp.</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Gets or sets the UTC delivery timestamp; <see langword="null"/> while pending.</summary>
    public DateTime? DeliveredAt { get; set; }
}
