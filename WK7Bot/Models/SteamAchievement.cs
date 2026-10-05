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
    public DateTimeOffset? UnlockTime { get; set; }
    
    /// <summary>
    /// Gets or sets the absolute URL pointing to the achievement icon graphic.
    /// </summary>
    public string IconUrl { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether the achievement is hidden on the Steam community site.
    /// </summary>
    public bool Hidden { get; set; }

    /// <summary>
    /// Gets or sets the global percentage of Steam players who unlocked this achievement (0 when unknown).
    /// </summary>
    public double PercentUnlocked { get; set; }
}