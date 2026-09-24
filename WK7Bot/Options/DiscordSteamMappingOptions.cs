namespace WK7Bot.Options;

using Microsoft.Extensions.Configuration;

/// <summary>
/// Defines a mapping configuration entry binding a Discord user ID to a 64-bit Steam ID.
/// </summary>
public class DiscordSteamMappingOptions
{
    /// <summary>
    /// Gets or sets the target Discord user snowflake ID.
    /// </summary>
    [ConfigurationKeyName("discord_user_id")]
    public string DiscordUserId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the associated 64-bit Steam ID.
    /// </summary>
    [ConfigurationKeyName("steam_id")]
    public string SteamId { get; set; } = string.Empty;
}