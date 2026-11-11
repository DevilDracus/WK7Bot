namespace WK7Bot.Models;

/// <summary>
/// Lightweight Steam activity snapshot for a single user, used by the friend-activity digest.
/// </summary>
public class SteamUserActivity
{
    /// <summary>
    /// Gets or sets the 64-bit Steam ID.
    /// </summary>
    public string SteamId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the Steam persona name.
    /// </summary>
    public string PersonaName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the human-readable online status.
    /// </summary>
    public string PersonaState { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the title of the currently played game, if any.
    /// </summary>
    public string? CurrentGameTitle { get; set; }

    /// <summary>
    /// Gets or sets the AppID of the currently played game, if any.
    /// </summary>
    public uint? CurrentGameAppId { get; set; }

    /// <summary>
    /// Gets or sets the URL of the user's full-size Steam avatar, if available.
    /// </summary>
    public string? SteamAvatarUrl { get; set; }

    /// <summary>
    /// Gets the games played in the last two weeks.
    /// </summary>
    public List<SteamRecentGame> RecentGames { get; set; } = new();
}
