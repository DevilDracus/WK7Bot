namespace WK7Bot.Models;

using System;
using System.Collections.Generic;

/// <summary>
/// Aggregates Steam profile status, play statistics, and achievement progress.
/// </summary>
public class SteamUserData
{
    /// <summary>
    /// Gets or sets the unique 64-bit Steam ID identifier.
    /// </summary>
    public string SteamId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the player's Steam persona display name.
    /// </summary>
    public string PersonaName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the current Steam online persona status.
    /// </summary>
    public string PersonaState { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the name of the active game currently being played on Steam.
    /// </summary>
    public string? CurrentGameTitle { get; set; }

    /// <summary>
    /// Gets or sets the Steam AppID of the active game being played.
    /// </summary>
    public uint? CurrentGameAppId { get; set; }

    /// <summary>
    /// Gets or sets the total play time in minutes across all games in the last 14 days.
    /// </summary>
    public int PlaytimeLastTwoWeeksMinutes { get; set; }

    /// <summary>
    /// Gets or sets the collection of games played recently by the user.
    /// </summary>
    public List<SteamRecentGame> RecentGames { get; set; } = new();

    /// <summary>
    /// Gets or sets the list of recently unlocked achievements for the currently active game.
    /// </summary>
    public List<SteamAchievement> RecentAchievements { get; set; } = new();
}