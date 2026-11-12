namespace WK7Bot.Services.Interfaces;

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using WK7Bot.Models;

/// <summary>
/// Defines operations for requesting user profiles, activity stats, and achievements from the Steam Web API.
/// </summary>
public interface ISteamService
{
    /// <summary>
    /// Resolves the mapped 64-bit Steam ID for a given Discord user ID from configuration options.
    /// </summary>
    /// <param name="discordUserId">The target Discord user snowflake ID string.</param>
    /// <returns>The mapped 64-bit Steam ID, or null if no mapping exists.</returns>
    string? GetSteamIdForDiscordUser(string discordUserId);

    /// <summary>
    /// Fetches player summary data, recent game playtimes, and active game achievements for a specific Steam ID.
    /// </summary>
    /// <param name="steamId">The 64-bit Steam ID of the user.</param>
    /// <param name="cancellationToken">A cancellation token to monitor for task cancellation.</param>
    /// <returns>A populated <see cref="SteamUserData"/> model or null if fetching fails.</returns>
    Task<SteamUserData?> GetSteamUserDataAsync(string steamId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves player achievements for a specified game and merges human-readable titles, descriptions, and icon image URLs from the Steam Game Schema API.
    /// </summary>
    /// <param name="steamId">The unique 64-bit Steam identifier of the target user.</param>
    /// <param name="appId">The unique application identifier for the target game.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A collection of enriched <see cref="SteamAchievement"/> instances containing achievement metadata, timestamps, and icon URLs.</returns>
    Task<IReadOnlyList<SteamAchievement>> GetEnrichedAchievementsAsync(
        string steamId,
        uint appId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves the list of games owned by the specified Steam user.
    /// </summary>
    /// <param name="steamId">The 64-bit Steam identifier of the user.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A collection of owned games as (AppId, Name) tuples.</returns>
    Task<IReadOnlyList<(uint AppId, string Name)>> GetOwnedGamesAsync(
        string steamId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetches lightweight activity summaries for multiple Steam IDs in a single batch.
    /// </summary>
    /// <param name="steamIds">The 64-bit Steam IDs to query.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A collection of <see cref="SteamUserActivity"/> snapshots containing current and recent game activity.</returns>
    Task<IReadOnlyList<SteamUserActivity>> GetUserActivitiesAsync(
        IEnumerable<string> steamIds,
        CancellationToken cancellationToken = default);
}
