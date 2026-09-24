namespace WK7Bot.Options;

/// <summary>
/// Toggles indicating enabled or disabled application feature modules.
/// </summary>
public class FeatureOptions
{
    /// <summary>
    /// Gets or sets a value indicating whether RSS feed background polling is enabled.
    /// </summary>
    public bool RssPollingEnabled { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether Home Assistant notification listener is enabled.
    /// </summary>
    public bool HomeAssistantNotifierEnabled { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether Leipzig waste collection schedule tracking is enabled.
    /// </summary>
    public bool LeipzigWasteEnabled { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether Alexa notification dispatching is enabled.
    /// </summary>
    public bool AlexaNotificationsEnabled { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether publishing Discord presence updates to MQTT is enabled.
    /// </summary>
    public bool DiscordPresenceMqttEnabled { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether Steam presence enrichment is enabled.
    /// </summary>
    public bool SteamPresenceEnabled { get; set; }
}