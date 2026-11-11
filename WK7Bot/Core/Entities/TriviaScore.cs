namespace WK7Bot.Core.Entities;

/// <summary>
/// A user's trivia score for a given week.
/// </summary>
public class TriviaScore
{
    /// <summary>Gets or sets the unique database identifier.</summary>
    public int Id { get; set; }

    /// <summary>Gets or sets the Discord user.</summary>
    public ulong DiscordUserId { get; set; }

    /// <summary>Gets or sets the guild the score belongs to.</summary>
    public ulong GuildId { get; set; }

    /// <summary>Gets or sets the accumulated score for the week.</summary>
    public int Score { get; set; }

    /// <summary>Gets or sets the Monday (UTC) that starts the scoring week.</summary>
    public DateTime WeekStart { get; set; }

    /// <summary>Gets or sets the UTC timestamp of the last update.</summary>
    public DateTime LastPlayedAt { get; set; } = DateTime.UtcNow;
}
