namespace WK7Bot.Core.Entities;

/// <summary>
/// Represents a Discord user's linked external gaming account (Steam or Battle.net).
/// Links created through <c>/link</c> are the runtime source of truth; the add-on options are
/// updated as a best-effort mirror so the mapping survives a configuration export.
/// </summary>
public class UserAccountLink
{
    /// <summary>Gets or sets the unique database identifier.</summary>
    public int Id { get; set; }

    /// <summary>Gets or sets the Discord user identifier that owns this link.</summary>
    public ulong DiscordUserId { get; set; }

    /// <summary>Gets or sets the provider key: <c>steam</c> or <c>battlenet</c>.</summary>
    public string Provider { get; set; } = string.Empty;

    /// <summary>Gets or sets the provider-specific identifier (SteamID64 or BattleTag).</summary>
    public string ExternalId { get; set; } = string.Empty;

    /// <summary>Gets or sets the optional human-readable display name.</summary>
    public string? DisplayName { get; set; }

    /// <summary>Gets or sets the optional Battle.net OAuth refresh token.</summary>
    public string? RefreshToken { get; set; }

    /// <summary>Gets or sets the optional Battle.net region (eu, us, kr, tw).</summary>
    public string? Region { get; set; }

    /// <summary>Gets or sets the optional Steam wishlist URL supplied by the user.</summary>
    public string? WishlistUrl { get; set; }

    /// <summary>Gets or sets the UTC timestamp when the link was created.</summary>
    public DateTime LinkedAt { get; set; } = DateTime.UtcNow;
}
