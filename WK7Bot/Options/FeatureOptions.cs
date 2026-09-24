namespace WK7Bot.Options;

using Microsoft.Extensions.Configuration;

/// <summary>
/// Toggles indicating enabled or disabled application feature modules.
/// </summary>
public class FeatureOptions
{
    /// <summary>
    /// Gets or sets a value indicating whether RSS feed background polling is enabled.
    /// </summary>
    [ConfigurationKeyName("rss_polling_enabled")]
    public bool RssPollingEnabled { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether Home Assistant notification listener is enabled.
    /// </summary>
    [ConfigurationKeyName("home_assistant_notifier_enabled")]
    public bool HomeAssistantNotifierEnabled { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether Leipzig waste collection schedule tracking is enabled.
    /// </summary>
    [ConfigurationKeyName("leipzig_waste_enabled")]
    public bool LeipzigWasteEnabled { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether Alexa notification dispatching is enabled.
    /// </summary>
    [ConfigurationKeyName("alexa_notifications_enabled")]
    public bool AlexaNotificationsEnabled { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether publishing Discord presence updates to MQTT is enabled.
    /// </summary>
    [ConfigurationKeyName("discord_presence_mqtt_enabled")]
    public bool DiscordPresenceMqttEnabled { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether Steam presence enrichment is enabled.
    /// </summary>
    [ConfigurationKeyName("steam_presence_enabled")]
    public bool SteamPresenceEnabled { get; set; }
}