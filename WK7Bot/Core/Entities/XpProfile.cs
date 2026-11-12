namespace WK7Bot.Core.Entities;

/// <summary>
/// A user's XP and level within a guild.
/// </summary>
public class XpProfile
{
    /// <summary>Gets or sets the unique database identifier.</summary>
    public int Id { get; set; }

    /// <summary>Gets or sets the Discord user.</summary>
    public ulong DiscordUserId { get; set; }

    /// <summary>Gets or sets the guild the profile belongs to.</summary>
    public ulong GuildId { get; set; }

    /// <summary>Gets or sets the accumulated XP.</summary>
    public int Xp { get; set; }

    /// <summary>Gets or sets the current level.</summary>
    public int Level { get; set; }

    /// <summary>Gets or sets the UTC timestamp of the last XP award (for the 60-second cooldown).</summary>
    public DateTime LastXpAt { get; set; } = DateTime.MinValue;

    /// <summary>Gets or sets the total voice minutes tracked for this user.</summary>
    public int VoiceMinutes { get; set; }
}
