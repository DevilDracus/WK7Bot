namespace WK7Bot.Options;

using System.Collections.Generic;

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
    public string DiscordToken { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the MQTT broker hostname or container address.
    /// </summary>
    public string MqttHost { get; set; } = "core-mosquitto";

    /// <summary>
    /// Gets or sets the connection port for the MQTT broker.
    /// </summary>
    public int MqttPort { get; set; } = 1883;

    /// <summary>
    /// Gets or sets the optional username for MQTT broker authentication.
    /// </summary>
    public string? MqttUsername { get; set; }

    /// <summary>
    /// Gets or sets the optional password for MQTT broker authentication.
    /// </summary>
    public string? MqttPassword { get; set; }

    /// <summary>
    /// Gets or sets the Valve Steam Web API key.
    /// </summary>
    public string? SteamApiKey { get; set; }

    /// <summary>
    /// Gets or sets the list of explicit Discord user ID to Steam ID mappings.
    /// </summary>
    public List<DiscordSteamMappingOptions> DiscordSteamMappings { get; set; } = new();

    /// <summary>
    /// Gets or sets the target Discord user IDs configured for direct messaging notifications.
    /// </summary>
    public List<string> DiscordDmUserIds { get; set; } = new();

    /// <summary>
    /// Gets or sets the configuration options for Alexa notifications.
    /// </summary>
    public AlexaNotificationOptions AlexaNotification { get; set; } = new();

    /// <summary>
    /// Gets or sets feature toggle flags for individual background services.
    /// </summary>
    public FeatureOptions Features { get; set; } = new();
}