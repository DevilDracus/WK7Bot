namespace WK7Bot.Options;

/// <summary>
/// The snake_case feature keys used by configuration (<c>features.*</c>) and by
/// <c>servers.testing_features</c>, in one place so the option attributes, the DI registration and
/// the automatic target routing cannot drift apart.
/// </summary>
public static class FeatureKeys
{
    /// <summary>Key of the RSS polling feature.</summary>
    public const string RssPolling = "rss_polling_enabled";

    /// <summary>Key of the Home Assistant notifier feature.</summary>
    public const string HomeAssistantNotifier = "home_assistant_notifier_enabled";

    /// <summary>Key of the Leipzig waste reminders feature.</summary>
    public const string LeipzigWaste = "leipzig_waste_enabled";

    /// <summary>Key of the Alexa mention notifications feature.</summary>
    public const string AlexaNotifications = "alexa_notifications_enabled";

    /// <summary>Key of the Discord presence MQTT bridge feature.</summary>
    public const string DiscordPresenceMqtt = "discord_presence_mqtt_enabled";

    /// <summary>Key of the Steam presence enrichment feature.</summary>
    public const string SteamPresence = "steam_presence_enabled";

    /// <summary>Key of the Battle.net presence enrichment feature.</summary>
    public const string BattleNetPresence = "battlenet_presence_enabled";

    /// <summary>Key of the monthly produce and weekly recipe publisher feature.</summary>
    public const string FoodService = "food_service_enabled";

    /// <summary>Key of the spontaneous meetup expiry feature.</summary>
    public const string SpontanTreff = "spontan_treff_enabled";

    /// <summary>Key of the weekend digest feature.</summary>
    public const string WeekendDigest = "weekend_digest_enabled";

    /// <summary>Key of the DWD rain and storm warning feature.</summary>
    public const string DwdWarning = "dwd_warning_enabled";

    /// <summary>Key of the error notification feature.</summary>
    public const string ErrorNotifications = "error_notifications_enabled";

    /// <summary>
    /// The keys of the features that post automatically and therefore honour
    /// <c>servers.testing_features</c> routing.
    /// </summary>
    public static class RoutedFeatures
    {
        /// <summary>Routing key of the Leipzig waste reminders.</summary>
        public const string LeipzigWaste = "leipzig_waste";

        /// <summary>Routing key of the food publisher.</summary>
        public const string FoodService = "food_service";

        /// <summary>Routing key of the weekend digest.</summary>
        public const string WeekendDigest = "weekend_digest";

        /// <summary>Routing key of the DWD warnings.</summary>
        public const string DwdWarning = "dwd_warning";

        /// <summary>Routing key of the error notifications.</summary>
        public const string ErrorNotifications = "error_notifications";
    }
}
