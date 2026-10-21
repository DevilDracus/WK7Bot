namespace WK7Bot.Services.Interfaces;

/// <summary>
/// Resolves the effective Steam and Battle.net account links by merging the static add-on option
/// mappings with the links created at runtime through <c>/link</c> and <c>/wishlist-add</c>.
/// Runtime links win, so a newly linked account takes effect without a restart.
/// </summary>
public interface IAccountLinkResolver
{
    /// <summary>Returns all effective Steam links.</summary>
    Task<IReadOnlyList<SteamAccountLink>> GetSteamLinksAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns all effective Battle.net links.</summary>
    Task<IReadOnlyList<BattleNetAccountLink>> GetBattleNetLinksAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns the effective Steam ID for one Discord user, or <see langword="null"/>.</summary>
    Task<string?> GetSteamIdAsync(ulong discordUserId, CancellationToken cancellationToken = default);

    /// <summary>Returns the effective Steam link (including wishlist URL) for one Discord user, or <see langword="null"/>.</summary>
    Task<SteamAccountLink?> GetSteamLinkAsync(ulong discordUserId, CancellationToken cancellationToken = default);

    /// <summary>Returns the effective Battle.net link for one Discord user, or <see langword="null"/>.</summary>
    Task<BattleNetAccountLink?> GetBattleNetLinkAsync(ulong discordUserId, CancellationToken cancellationToken = default);
}

/// <summary>An effective Discord → Steam link.</summary>
/// <param name="DiscordUserId">The Discord user ID.</param>
/// <param name="SteamId">The 64-bit Steam ID.</param>
/// <param name="WishlistUrl">The optional shared wishlist URL.</param>
public sealed record SteamAccountLink(ulong DiscordUserId, string SteamId, string? WishlistUrl = null);

/// <summary>An effective Discord → Battle.net link.</summary>
/// <param name="DiscordUserId">The Discord user ID.</param>
/// <param name="RefreshToken">The OAuth refresh token.</param>
/// <param name="Region">The optional region.</param>
/// <param name="BattleTag">The optional BattleTag.</param>
public sealed record BattleNetAccountLink(ulong DiscordUserId, string RefreshToken, string? Region, string? BattleTag);
