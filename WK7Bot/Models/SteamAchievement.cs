namespace WK7Bot.Models;

/// <summary>
/// Represents an achievement unlocked by the player within a Steam application.
/// </summary>
public class SteamAchievement
{
    /// <summary>
    /// Gets or sets the internal API identifier of the achievement.
    /// </summary>
    public string ApiName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the localized display title of the achievement.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the localized description text explaining how the achievement was unlocked.
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the exact timestamp when the achievement was unlocked.
    /// </summary>
    public DateTimeOffset UnlockTime { get; set; }
}