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
    /// Gets or sets a value indicating whether Battle.net profile tracking is enabled.
    /// </summary>
    [ConfigurationKeyName("battlenet_presence_enabled")]
    public bool BattleNetPresenceEnabled { get; set; } = true;

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

    /// <summary>
    /// Gets or sets a value indicating whether the weekly Leipzig weekend digest with voting poll is posted.
    /// </summary>
    [ConfigurationKeyName("weekend_digest_enabled")]
    public bool WeekendDigestEnabled { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether local DWD rain and storm warnings are polled and posted.
    /// </summary>
    [ConfigurationKeyName("dwd_warning_enabled")]
    public bool DwdWarningEnabled { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether captured errors are additionally sent to Discord as direct messages.
    /// </summary>
    [ConfigurationKeyName("error_notifications_enabled")]
    public bool ErrorNotificationsEnabled { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether the bot publishes its own status (availability, uptime and
    /// per-feature health) to Home Assistant as MQTT discovery entities.
    /// </summary>
    [ConfigurationKeyName("mqtt_bot_status_enabled")]
    public bool MqttBotStatusEnabled { get; set; } = true;

    /// <summary>
    /// Resolves the enabled state of a feature key against the bound flags. Keys are matched
    /// case-insensitively; an unknown key is treated as enabled so a new key does not silently disable a
    /// feature on older configuration.
    /// </summary>
    /// <param name="featureKey">The snake_case feature key (e.g. <c>food_service_enabled</c>).</param>
    /// <returns><see langword="true"/> when the feature is enabled.</returns>
    public bool IsEnabled(string featureKey) => featureKey?.ToLowerInvariant() switch
    {
        FeatureKeys.RssPolling => RssPollingEnabled,
        FeatureKeys.HomeAssistantNotifier => HomeAssistantNotifierEnabled,
        FeatureKeys.LeipzigWaste => LeipzigWasteEnabled,
        FeatureKeys.AlexaNotifications => AlexaNotificationsEnabled,
        FeatureKeys.DiscordPresenceMqtt => DiscordPresenceMqttEnabled,
        FeatureKeys.SteamPresence => SteamPresenceEnabled,
        FeatureKeys.BattleNetPresence => BattleNetPresenceEnabled,
        FeatureKeys.FoodService => FoodServiceEnabled,
        FeatureKeys.SpontanTreff => SpontanTreffEnabled,
        FeatureKeys.WeekendDigest => WeekendDigestEnabled,
        FeatureKeys.DwdWarning => DwdWarningEnabled,
        FeatureKeys.ErrorNotifications => ErrorNotificationsEnabled,
        FeatureKeys.MqttBotStatus => MqttBotStatusEnabled,
        _ => true
    };
}
