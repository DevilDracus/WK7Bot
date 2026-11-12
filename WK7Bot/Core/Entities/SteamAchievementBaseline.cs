namespace WK7Bot.Core.Entities;

/// <summary>
/// Records the last known achievement unlock state for a Steam game owned by a linked Discord user,
/// so newly unlocked achievements can be detected on subsequent polling cycles.
/// </summary>
public class SteamAchievementBaseline
{
    /// <summary>Gets or sets the unique database identifier.</summary>
    public int Id { get; set; }

    /// <summary>Gets or sets the Discord user identifier that owns this baseline entry.</summary>
    public ulong DiscordUserId { get; set; }

    /// <summary>Gets or sets the 64-bit Steam identifier of the linked account.</summary>
    public string SteamId { get; set; } = string.Empty;

    /// <summary>Gets or sets the Steam application identifier of the game.</summary>
    public uint AppId { get; set; }

    /// <summary>Gets or sets the achievement API name (unique within a game).</summary>
    public string ApiName { get; set; } = string.Empty;

    /// <summary>Gets or sets whether the achievement was unlocked at the last check.</summary>
    public bool Achieved { get; set; }

    /// <summary>Gets or sets the timestamp the achievement was unlocked, when known.</summary>
    public DateTimeOffset? UnlockTime { get; set; }

    /// <summary>Gets or sets the UTC timestamp when this baseline row was last checked.</summary>
    public DateTimeOffset LastChecked { get; set; }
}
