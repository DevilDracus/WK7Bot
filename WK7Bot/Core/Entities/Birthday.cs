namespace WK7Bot.Core.Entities;

/// <summary>
/// A member's birthday, announced once per year by <c>BirthdayBackgroundService</c>.
/// </summary>
public class Birthday
{
    /// <summary>Gets or sets the unique database identifier.</summary>
    public int Id { get; set; }

    /// <summary>Gets or sets the Discord user the birthday belongs to.</summary>
    public ulong DiscordUserId { get; set; }

    /// <summary>Gets or sets the guild the birthday is announced in (0 when none configured).</summary>
    public ulong GuildId { get; set; }

    /// <summary>Gets or sets the birth month (1-12).</summary>
    public int Month { get; set; }

    /// <summary>Gets or sets the birth day (1-31).</summary>
    public int Day { get; set; }

    /// <summary>Gets or sets the UTC creation timestamp.</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Gets or sets the year the birthday was last announced (0 when never).</summary>
    public int LastAnnouncedYear { get; set; }
}
