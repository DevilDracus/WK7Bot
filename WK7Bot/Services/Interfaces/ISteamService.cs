namespace WK7Bot.Services.Interfaces;

using System.Threading;
using System.Threading.Tasks;
using WK7Bot.Models;

/// <summary>
/// Defines operations for requesting user profiles, activity stats, and achievements from the Steam Web API.
/// </summary>
public interface ISteamService
{
    /// <summary>
    /// Resolves the mapped 64-bit Steam ID for a given Discord user ID from options.
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
}