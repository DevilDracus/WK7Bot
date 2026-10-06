namespace WK7Bot.Options;

using Microsoft.Extensions.Configuration;

/// <summary>
/// Feature toggles for controlling background services.
/// </summary>
public class FeatureOptions
{
    /// <summary>
    /// Gets or sets a value indicating whether RSS feed polling is enabled.
    /// </summary>
    [ConfigurationKeyName("rss_polling_enabled")]
    public bool RssPollingEnabled { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether Home Assistant notification listener is enabled.
    /// </summary>
    [ConfigurationKeyName("home_assistant_notifier_enabled")]
    public bool HomeAssistantNotifierEnabled { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether Leipzig waste collection schedule tracking is enabled.
    /// </summary>
    [ConfigurationKeyName("leipzig_waste_enabled")]
    public bool LeipzigWasteEnabled { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether Alexa notifications are enabled.
    /// </summary>
    [ConfigurationKeyName("alexa_notifications_enabled")]
    public bool AlexaNotificationsEnabled { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether Discord presence syncing via MQTT is enabled.
    /// </summary>
    [ConfigurationKeyName("discord_presence_mqtt_enabled")]
    public bool DiscordPresenceMqttEnabled { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether Steam presence tracking is enabled.
    /// </summary>
    [ConfigurationKeyName("steam_presence_enabled")]
    public bool SteamPresenceEnabled { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether the monthly seasonal food and weekly recipe background service is enabled.
    /// </summary>
    [ConfigurationKeyName("food_service_enabled")]
    public bool FoodServiceEnabled { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether expired spontaneous meetups ("Spontan-Treff") are closed automatically.
    /// </summary>
    [ConfigurationKeyName("spontan_treff_enabled")]
    public bool SpontanTreffEnabled { get; set; } = true;
}
