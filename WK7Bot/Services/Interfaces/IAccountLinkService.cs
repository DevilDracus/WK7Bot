namespace WK7Bot.Services.Interfaces;

using WK7Bot.Core.Entities;

/// <summary>
/// Manages Discord user links to external gaming accounts and mirrors them into the add-on options.
/// </summary>
public interface IAccountLinkService
{
    /// <summary>Links a Steam account from a SteamID64, profile URL or vanity URL.</summary>
    Task<AccountLinkResult> LinkSteamAsync(ulong discordUserId, string input, CancellationToken cancellationToken = default);

    /// <summary>Links a Battle.net account from a refresh token.</summary>
    Task<AccountLinkResult> LinkBattleNetAsync(ulong discordUserId, string refreshToken, string? region, string? battleTag, CancellationToken cancellationToken = default);

    /// <summary>Links a Steam account from a Steam wishlist URL.</summary>
    Task<AccountLinkResult> LinkWishlistAsync(ulong discordUserId, string wishlistUrl, CancellationToken cancellationToken = default);

    /// <summary>Removes a provider link for a Discord user.</summary>
    Task<AccountLinkResult> UnlinkAsync(ulong discordUserId, string provider, CancellationToken cancellationToken = default);

    /// <summary>Returns all stored links for a Discord user.</summary>
    Task<IReadOnlyList<UserAccountLink>> GetLinksAsync(ulong discordUserId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of an account-link operation.
/// </summary>
/// <param name="Success">Whether the operation succeeded.</param>
/// <param name="Message">A human-readable result message.</param>
/// <param name="ExternalId">The resolved external identifier, when applicable.</param>
public sealed record AccountLinkResult(bool Success, string Message, string? ExternalId = null);
