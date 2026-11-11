namespace WK7Bot.Core.Entities;

/// <summary>
/// A single voice-channel session, recorded when a member leaves a voice channel.
/// </summary>
public class VoiceSession
{
    /// <summary>Gets or sets the unique database identifier.</summary>
    public int Id { get; set; }

    /// <summary>Gets or sets the Discord user who was in voice.</summary>
    public ulong DiscordUserId { get; set; }

    /// <summary>Gets or sets the guild the session happened in.</summary>
    public ulong GuildId { get; set; }

    /// <summary>Gets or sets the voice channel that was joined.</summary>
    public ulong ChannelId { get; set; }

    /// <summary>Gets or sets the UTC join time.</summary>
    public DateTime JoinedAt { get; set; }

    /// <summary>Gets or sets the UTC leave time.</summary>
    public DateTime LeftAt { get; set; }

    /// <summary>Gets or sets the session duration in seconds.</summary>
    public int DurationSeconds { get; set; }
}
