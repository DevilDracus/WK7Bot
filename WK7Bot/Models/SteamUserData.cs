namespace WK7Bot.Models;

using System;
using System.Collections.Generic;
using System.Globalization;

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
    /// Gets or sets the absolute URL of the player's Steam avatar (full size), or null when unavailable.
    /// </summary>
    public string? SteamAvatarUrl { get; set; }

    /// <summary>
    /// Gets the total play time in minutes across all games in the last 14 days.
    /// </summary>
    public int PlaytimeLastTwoWeeksMinutes { get; set; }

    /// <summary>
    /// Gets the localized display representation of <see cref="PlaytimeLastTwoWeeksMinutes"/> (e.g. "15,8 Std.", "45 Min.", "0 Std.").
    /// </summary>
    public string PlaytimeDisplay => FormatPlaytime(PlaytimeLastTwoWeeksMinutes);

    /// <summary>
    /// Gets or sets the number of achievements unlocked in the currently active game (0 when unknown).
    /// </summary>
    public int CurrentGameAchievementsUnlocked { get; set; }

    /// <summary>
    /// Gets or sets the total number of achievements defined for the currently active game (0 when unknown).
    /// </summary>
    public int CurrentGameAchievementsTotal { get; set; }

    /// <summary>
    /// Gets or sets the collection of games played recently by the user.
    /// </summary>
    public List<SteamRecentGame> RecentGames { get; set; } = new();

    /// <summary>
    /// Gets or sets the list of recently unlocked achievements for the currently active game.
    /// </summary>
    public List<SteamAchievement> RecentAchievements { get; set; } = new();

    /// <summary>
    /// Formats a play time duration in minutes as a human-readable German string.
    /// </summary>
    /// <param name="minutes">The total number of minutes to format.</param>
    /// <returns>A localized string such as "0 Std.", "45 Min." or "15,8 Std.".</returns>
    public static string FormatPlaytime(int minutes)
    {
        if (minutes <= 0)
        {
            return "0 Std.";
        }

        if (minutes < 60)
        {
            return string.Create(CultureInfo.GetCultureInfo("de-DE"), $"{minutes} Min.");
        }

        return string.Create(CultureInfo.GetCultureInfo("de-DE"), $"{minutes / 60.0:0.#} Std.");
    }
}