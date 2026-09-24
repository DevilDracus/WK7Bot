namespace WK7Bot.Models;

/// <summary>
/// Details recent gameplay duration and metadata for a specific Steam application.
/// </summary>
public class SteamRecentGame
{
    /// <summary>
    /// Gets or sets the unique Steam application identifier.
    /// </summary>
    public uint AppId { get; set; }

    /// <summary>
    /// Gets or sets the display title of the game.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the total minutes played in the last two weeks.
    /// </summary>
    public int PlaytimeTwoWeeksMinutes { get; set; }

    /// <summary>
    /// Gets or sets the total lifetime minutes played on record.
    /// </summary>
    public int PlaytimeForeverMinutes { get; set; }
}