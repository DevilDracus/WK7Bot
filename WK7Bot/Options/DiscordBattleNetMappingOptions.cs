namespace WK7Bot.Options;

using Microsoft.Extensions.Configuration;

/// <summary>
/// Defines a mapping configuration entry binding a Discord user ID to a Battle.net account,
/// identified by the OAuth refresh token of that account.
/// </summary>
public class DiscordBattleNetMappingOptions
{
    /// <summary>
    /// Gets or sets the target Discord user snowflake ID.
    /// </summary>
    [ConfigurationKeyName("discord_user_id")]
    public string DiscordUserId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the OAuth refresh token issued for the Battle.net account. The bot exchanges it
    /// for a short-lived access token, so it never stores user credentials.
    /// </summary>
    [ConfigurationKeyName("refresh_token")]
    public string RefreshToken { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the optional API region for this account (us, eu, kr, tw). Falls back to the
    /// global <c>battlenet_region</c> setting when empty.
    /// </summary>
    [ConfigurationKeyName("region")]
    public string Region { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets an optional BattleTag fallback used when the OAuth userinfo endpoint cannot be
    /// reached (it is also required by the Diablo III profile endpoint).
    /// </summary>
    [ConfigurationKeyName("battle_tag")]
    public string BattleTag { get; set; } = string.Empty;
}
