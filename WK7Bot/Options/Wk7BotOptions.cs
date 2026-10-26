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
    public string DiscordToken { get; init; } = string.Empty;

    /// <summary>
    /// Gets or sets the minimum log level for the bot's console output (Debug, Information, Warning, Error).
    /// </summary>

    /// <summary>
    /// Gets or sets the MQTT broker hostname or container address.
    /// </summary>
    [ConfigurationKeyName("mqtt_host")]
    public string MqttHost { get; init; } = "core-mosquitto";

    /// <summary>
    /// Gets or sets the connection port for the MQTT broker.
    /// </summary>
    [ConfigurationKeyName("mqtt_port")]
    public int MqttPort { get; init; } = 1883;

    /// <summary>
    /// Gets or sets the optional username for MQTT broker authentication.
    /// </summary>
    [ConfigurationKeyName("mqtt_username")]
    public string? MqttUsername { get; init; }

    /// <summary>
    /// Gets or sets the optional password for MQTT broker authentication.
    /// </summary>
    [ConfigurationKeyName("mqtt_password")]
    public string? MqttPassword { get; init; }

    /// <summary>
    /// Gets or sets the Valve Steam Web API key.
    /// </summary>
    [ConfigurationKeyName("steam_api_key")]
    public string? SteamApiKey { get; init; }

    /// <summary>
    /// Gets or sets the Google Gemini API key used for generating seasonal produce and tailored renal recipes.
    /// </summary>
    [ConfigurationKeyName("gemini_api_key")]
    public string? GeminiApiKey { get; init; }

    /// <summary>
    /// Gets or sets the OAuth client ID of the Battle.net application registered on develop.battle.net.
    /// </summary>
    [ConfigurationKeyName("battlenet_client_id")]
    public string? BattleNetClientId { get; init; }

    /// <summary>
    /// Gets or sets the OAuth client secret of the Battle.net application registered on develop.battle.net.
    /// </summary>
    [ConfigurationKeyName("battlenet_client_secret")]
    public string? BattleNetClientSecret { get; init; }

    /// <summary>
    /// Gets or sets the Battle.net API region (us, eu, kr, tw) used for OAuth and game data requests.
    /// </summary>
    [ConfigurationKeyName("battlenet_region")]
    public string BattleNetRegion { get; init; } = "eu";

    /// <summary>
    /// Gets or sets the Battle.net API locale (e.g. de_DE) used to localize class, faction and realm names.
    /// </summary>
    [ConfigurationKeyName("battlenet_locale")]
    public string BattleNetLocale { get; init; } = "de_DE";

    /// <summary>
    /// Gets or sets the list of explicit Discord user ID to Battle.net account mappings.
    /// </summary>
    [ConfigurationKeyName("discord_battlenet_mappings")]
    public List<DiscordBattleNetMappingOptions> DiscordBattleNetMappings { get; init; } = new();

    /// <summary>
    /// Gets or sets the list of explicit Discord user ID to Steam ID mappings.
    /// </summary>
    [ConfigurationKeyName("discord_steam_mappings")]
    public List<DiscordSteamMappingOptions> DiscordSteamMappings { get; init; } = new();

    /// <summary>
    /// Gets or sets the target Discord user IDs configured for direct messaging notifications.
    /// </summary>
    [ConfigurationKeyName("discord_dm_user_ids")]
    public List<string> DiscordDmUserIds { get; init; } = new();

    /// <summary>
    /// Gets or sets the target Discord user IDs that receive captured errors as direct message embeds.
    /// </summary>
    [ConfigurationKeyName("error_notify_user_ids")]
    public List<string> ErrorNotifyUserIds { get; init; } = new();

    /// <summary>
    /// Gets or sets the configuration options for Alexa notifications.
    /// </summary>
    [ConfigurationKeyName("alexa_notification")]
    public AlexaNotificationOptions AlexaNotification { get; init; } = new();

    /// <summary>
    /// Gets or sets the configuration options for the local DWD rain and storm warning feature.
    /// </summary>
    [ConfigurationKeyName("dwd_warning")]
    public DwdWarningOptions DwdWarning { get; init; } = new();

    /// <summary>
    /// Gets or sets the Discord servers that receive automatic messages (WK7 server, bot test server).
    /// </summary>
    [ConfigurationKeyName("servers")]
    public ServersOptions Servers { get; init; } = new();

    /// <summary>
    /// Gets or sets feature toggle flags for individual background services.
    /// </summary>
    [ConfigurationKeyName("features")]
    public FeatureOptions Features { get; init; } = new();

    /// <summary>
    /// Gets or sets a value indicating whether the startup configuration validator posts a full report
    /// even when all checks pass. When <see langword="false"/> (default), only problems are reported.
    /// </summary>
    [ConfigurationKeyName("startup_report_verbose")]
    public bool StartupReportVerbose { get; init; } = false;

    /// <summary>
    /// Gets or sets the Discord channel name where wishlist deal alerts are posted.
    /// </summary>
    [ConfigurationKeyName("deal_alert_channel_name")]
    public string DealAlertChannelName { get; init; } = "\U0001F3AEgame-deals";

    /// <summary>
    /// Gets or sets the interval in hours between wishlist deal checks.
    /// </summary>
    [ConfigurationKeyName("deal_check_interval_hours")]
    public int DealCheckIntervalHours { get; init; } = 12;

    /// <summary>
    /// Gets or sets the interval in hours between per-user deal watchlist checks.
    /// </summary>
    [ConfigurationKeyName("deal_watchlist_check_interval_hours")]
    public int DealWatchlistCheckIntervalHours { get; init; } = 6;

    /// <summary>
    /// Gets or sets the minimum discount percentage required for a per-user deal watchlist alert.
    /// </summary>
    [ConfigurationKeyName("deal_watchlist_min_discount_percent")]
    public int DealWatchlistMinDiscountPercent { get; init; } = 40;

    /// <summary>
    /// Gets or sets the minimum discount percentage required to trigger a non-historical-low deal alert.
    /// </summary>
    [ConfigurationKeyName("deal_min_discount_percent")]
    public int DealMinDiscountPercent { get; init; } = 60;

    /// <summary>
    /// Gets or sets the Home Assistant weather entity used by the /weather command.
    /// </summary>
    [ConfigurationKeyName("weather_entity")]
    public string WeatherEntity { get; init; } = "weather.home";

    /// <summary>
    /// Gets or sets the DWD warning poll interval in minutes.
    /// </summary>
    [ConfigurationKeyName("dwd_poll_interval_minutes")]
    public int DwdPollIntervalMinutes { get; init; } = 2;

    /// <summary>
    /// Gets or sets the ICS feed URL for Leipzig waste collection schedules.
    /// </summary>
    [ConfigurationKeyName("leipzig_waste_ics_feed_url")]
    public string LeipzigWasteIcsFeedUrl { get; init; } = string.Empty;

    /// <summary>
    /// Gets or sets the DWD pollen region name used by /pollen (substring match against the feed's
    /// region names, e.g. "Sachsen").
    /// </summary>
    [ConfigurationKeyName("pollen_region_name")]
    public string PollenRegionName { get; init; } = "Sachsen";

    /// <summary>
    /// Gets or sets the DWD pollen part-region name used by /pollen (substring match, e.g.
    /// "Tiefland Sachsen" for Leipzig).
    /// </summary>
    [ConfigurationKeyName("pollen_partregion_name")]
    public string PollenPartRegionName { get; init; } = "Tiefland Sachsen";

    /// <summary>
    /// Gets or sets the city whose DWD UV index is shown by /pollen.
    /// </summary>
    [ConfigurationKeyName("uv_city")]
    public string UvCity { get; init; } = "Leipzig";

    /// <summary>
    /// Gets or sets the Discord channel name that receives the daily pollen/UV post.
    /// </summary>
    [ConfigurationKeyName("pollen_channel_name")]
    public string PollenChannelName { get; init; } = "\u26c8\ufe0fweather-warnings";

    /// <summary>
    /// Gets or sets the local time (HH:mm) of the daily pollen/UV post.
    /// </summary>
    [ConfigurationKeyName("pollen_post_time")]
    public string PollenPostTime { get; init; } = "07:00";

    /// <summary>
    /// Gets or sets a value indicating whether the Prometheus /metrics endpoint is exposed.
    /// </summary>
    [ConfigurationKeyName("metrics_enabled")]
    public bool MetricsEnabled { get; init; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether /link writes new mappings back to the add-on options.
    /// </summary>
    [ConfigurationKeyName("link_auto_write_options")]
    public bool LinkAutoWriteOptions { get; init; } = true;

    /// <summary>
    /// Gets or sets the Discord channel name for the weekly leaderboard post.
    /// </summary>
    [ConfigurationKeyName("leaderboard_channel_name")]
    public string LeaderboardChannelName { get; init; } = "\U0001F3C6leaderboard";

    /// <summary>
    /// Gets or sets the Discord channel name for food publishing.
    /// </summary>
    [ConfigurationKeyName("food_channel_name")]
    public string FoodChannelName { get; init; } = "\U0001F34Efood";

    /// <summary>
    /// Gets or sets the Discord channel name for spontaneous meetups.
    /// </summary>
    [ConfigurationKeyName("spontan_treff_channel_name")]
    public string SpontanTreffChannelName { get; init; } = "\U0001F389spontan-treff";

    /// <summary>
    /// Gets or sets the Discord channel name for Leipzig waste notifications.
    /// </summary>
    [ConfigurationKeyName("leipzig_waste_channel_name")]
    public string LeipzigWasteChannelName { get; init; } = "\U0001F5D1\U0000FE0Fleipzig-waste";

    /// <summary>
    /// Gets or sets the Discord channel name for the weekend digest.
    /// </summary>
    [ConfigurationKeyName("weekend_digest_channel_name")]
    public string WeekendDigestChannelName { get; init; } = "\U0001F4C5wochenende";

    /// <summary>
    /// Gets or sets the Discord channel name for DWD weather warnings.
    /// </summary>
    [ConfigurationKeyName("dwd_warning_channel_name")]
    public string DwdWarningChannelName { get; init; } = "\U000026C8\U0000FE0Fweather-warnings";

    /// <summary>
    /// Gets or sets the Discord channel name for error notifications.
    /// </summary>
    [ConfigurationKeyName("error_channel_name")]
    public string ErrorChannelName { get; init; } = "\U0001F916WK7Errors\U00002757";

    /// <summary>
    /// Gets or sets the weekday of the weekly leaderboard post (e.g. Monday).
    /// </summary>
    [ConfigurationKeyName("leaderboard_post_weekday")]
    public string LeaderboardPostWeekday { get; init; } = "Monday";

    /// <summary>
    /// Gets or sets the local time (HH:mm) of the weekly leaderboard post.
    /// </summary>
    [ConfigurationKeyName("leaderboard_post_time")]
    public string LeaderboardPostTime { get; init; } = "18:00";

    /// <summary>
    /// HTTP timeout ceilings. Each typed client registers its timeout from this value when an explicit
    /// DI configuration is missing; explicit registration still wins.
    /// </summary>
    [ConfigurationKeyName("http_timeouts")]
    public Wk7BotHttpClientTimeouts HttpTimeouts { get; init; } = new();
}

/// <summary>
/// 3.1: Centralized HTTP timeout ceilings, grouped by expected latency.
/// </summary>
public sealed class Wk7BotHttpClientTimeouts
{
    /// <summary>Fast-path ceiling (seconds): health probes, Alexa notifications, HA supervisor proxy.</summary>
    [ConfigurationKeyName("fast")]
    public int FastSeconds { get; init; } = 10;

    /// <summary>Default ceiling (seconds): API-grade HTTP clients.</summary>
    [ConfigurationKeyName("default")]
    public int DefaultSeconds { get; init; } = 30;

    /// <summary>Slow ceiling (seconds): DWD warnings, recipe fetching — long-form payloads.</summary>
    [ConfigurationKeyName("slow")]
    public int SlowSeconds { get; init; } = 45;
}