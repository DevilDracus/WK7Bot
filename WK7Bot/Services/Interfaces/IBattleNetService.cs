namespace WK7Bot.Services.Interfaces;

using System.Threading;
using System.Threading.Tasks;
using WK7Bot.Models;
using WK7Bot.Options;

/// <summary>
/// Defines operations for resolving Discord to Battle.net account mappings and for requesting
/// account, World of Warcraft and Diablo profile data from the Battle.net API.
/// </summary>
public interface IBattleNetService
{
    /// <summary>
    /// Resolves the configured Battle.net mapping for a given Discord user ID from configuration options.
    /// </summary>
    /// <param name="discordUserId">The target Discord user snowflake ID string.</param>
    /// <returns>The matching mapping, or null if no mapping exists.</returns>
    DiscordBattleNetMappingOptions? GetMappingForDiscordUser(string discordUserId);

    /// <summary>
    /// Exchanges the configured OAuth refresh token for an access token and fetches the account
    /// identity together with the World of Warcraft characters and Diablo heroes of that account.
    /// </summary>
    /// <param name="refreshToken">The OAuth refresh token of the mapped Battle.net account.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A populated <see cref="BattleNetUserData"/> model, or null if retrieval fails.</returns>
    Task<BattleNetUserData?> GetBattleNetUserDataAsync(string refreshToken, CancellationToken cancellationToken = default);
}
