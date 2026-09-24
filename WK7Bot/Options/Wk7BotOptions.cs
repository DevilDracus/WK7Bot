namespace WK7Bot.Options;

using System.Collections.Generic;
using Microsoft.Extensions.Configuration;

/// <summary>
/// Root configuration options for the WK7 Bot application.
/// </summary>
public class Wk7BotOptions
{
    /// <summary>
    /// Configuration section name inside appsettings.json or options.json.
    /// </summary>
    public const string SectionName = "Wk7Bot";

    /// <summary>
    /// Gets or sets the authentication token for the Discord bot account.
    /// </summary>
    [ConfigurationKeyName("discord_token")]
    public string DiscordToken { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the MQTT broker hostname or container address.
    /// </summary>
    [ConfigurationKeyName("mqtt_host")]
    public string MqttHost { get; set; } = "core-mosquitto";

    /// <summary>
    /// Gets or sets the connection port for the MQTT broker.
    /// </summary>
    [ConfigurationKeyName("mqtt_port")]
    public int MqttPort { get; set; } = 1883;

    /// <summary>
    /// Gets or sets the optional username for MQTT broker authentication.
    /// </summary>
    [ConfigurationKeyName("mqtt_username")]
    public string? MqttUsername { get; set; }

    /// <summary>
    /// Gets or sets the optional password for MQTT broker authentication.
    /// </summary>
    [ConfigurationKeyName("mqtt_password")]
    public string? MqttPassword { get; set; }

    /// <summary>
    /// Gets or sets the Valve Steam Web API key.
    /// </summary>
    [ConfigurationKeyName("steam_api_key")]
    public string? SteamApiKey { get; set; }

    /// <summary>
    /// Gets or sets the list of explicit Discord user ID to Steam ID mappings.
    /// </summary>
    [ConfigurationKeyName("discord_steam_mappings")]
    public List<DiscordSteamMappingOptions> DiscordSteamMappings { get; set; } = new();

    /// <summary>
    /// Gets or sets the target Discord user IDs configured for direct messaging notifications.
    /// </summary>
    [ConfigurationKeyName("discord_dm_user_ids")]
    public List<string> DiscordDmUserIds { get; set; } = new();

    /// <summary>
    /// Gets or sets the configuration options for Alexa notifications.
    /// </summary>
    [ConfigurationKeyName("alexa_notification")]
    public AlexaNotificationOptions AlexaNotification { get; set; } = new();

    /// <summary>
    /// Gets or sets feature toggle flags for individual background services.
    /// </summary>
    [ConfigurationKeyName("features")]
    public FeatureOptions Features { get; set; } = new();
}