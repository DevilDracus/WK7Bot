namespace WK7Bot.Core.Utilities;

using System;
using System.Collections.Generic;
using System.Text.Json;
using WK7Bot.Options;

/// <summary>
/// A feature that publishes its status to Home Assistant: its configuration key, the slug used in the
/// MQTT topics, and the display name shown as entity name in Home Assistant.
/// </summary>
/// <param name="FeatureKey">The feature key (see <see cref="FeatureKeys"/>).</param>
/// <param name="Slug">The MQTT topic slug of the feature.</param>
/// <param name="DisplayName">The human-readable name of the feature.</param>
public sealed record BotStatusFeature(string FeatureKey, string Slug, string DisplayName);

/// <summary>
/// Attributes published as JSON on the bot's status sensor topic.
/// </summary>
/// <param name="Version">The application version.</param>
/// <param name="StartedAtUtc">The UTC process start time.</param>
/// <param name="UptimeSeconds">The process uptime in seconds.</param>
/// <param name="DiscordConnection">The Discord gateway connection state.</param>
/// <param name="Guilds">The number of guilds the bot is connected to.</param>
/// <param name="FeaturesTotal">The number of monitored features.</param>
/// <param name="FeaturesProblem">The number of monitored features currently in problem state.</param>
public sealed record BotStatusAttributes(
    string Version,
    DateTime StartedAtUtc,
    long UptimeSeconds,
    string DiscordConnection,
    int Guilds,
    int FeaturesTotal,
    int FeaturesProblem);

/// <summary>
/// Topic layout, discovery payloads and state payloads of the Home Assistant entities the bot publishes
/// about itself (availability, bot status sensor and per-feature enabled/problem sensors). The builders
/// are pure so the hosted service and its tests can use them without an MQTT broker.
/// </summary>
public static class BotStatusEntities
{
    /// <summary>Root MQTT topic of all bot status topics.</summary>
    public const string TopicPrefix = "wk7bot";

    /// <summary>Home Assistant device identifier; also the discovery node name.</summary>
    public const string DeviceId = "wk7bot";

    /// <summary>Display name of the device the entities belong to.</summary>
    public const string DeviceName = "WK7 Bot";

    /// <summary>Retained availability topic of the whole bot.</summary>
    public const string AvailabilityTopic = TopicPrefix + "/status";

    /// <summary>Payload published while the bot is running.</summary>
    public const string AvailabilityOnlinePayload = "ONLINE";

    /// <summary>
    /// Payload the broker publishes on the availability topic when the bot disappears without
    /// disconnecting (process kill, network loss) via the connection's last-will testament.
    /// </summary>
    public const string AvailabilityLostPayload = "OFFLINE";

    /// <summary>How long a feature problem sensor survives without a new message before Home Assistant marks it unavailable.</summary>
    public const int ProblemExpireAfterSeconds = 300;

    /// <summary>How long the bot status sensor survives without a new message (the heartbeat runs every 30 seconds).</summary>
    public const int StatusExpireAfterSeconds = 120;

    /// <summary>
    /// The features whose health is published: every feature that does scheduled background work.
    /// </summary>
    public static readonly IReadOnlyList<BotStatusFeature> MonitoredFeatures = new[]
    {
        new BotStatusFeature(FeatureKeys.FoodService, "food", "Food Service"),
        new BotStatusFeature(FeatureKeys.RssPolling, "rss", "RSS Polling"),
        new BotStatusFeature(FeatureKeys.LeipzigWaste, "leipzig_waste", "Leipzig Waste"),
        new BotStatusFeature(FeatureKeys.WeekendDigest, "weekend_digest", "Weekend Digest"),
        new BotStatusFeature(FeatureKeys.DwdWarning, "dwd_warning", "DWD Warnings"),
        new BotStatusFeature(FeatureKeys.SpontanTreff, "spontan_treff", "Spontan-Treff")
    };

    /// <summary>State topic of a feature's enabled sensor.</summary>
    /// <param name="feature">The feature.</param>
    /// <returns>The MQTT topic.</returns>
    public static string EnabledStateTopic(BotStatusFeature feature)
        => $"{TopicPrefix}/feature/{feature.Slug}/enabled";

    /// <summary>State topic of a feature's problem sensor.</summary>
    /// <param name="feature">The feature.</param>
    /// <returns>The MQTT topic.</returns>
    public static string ProblemStateTopic(BotStatusFeature feature)
        => $"{TopicPrefix}/feature/{feature.Slug}/problem";

    /// <summary>Topic carrying the JSON attributes (last success, last failure, last error) of a feature's problem sensor.</summary>
    /// <param name="feature">The feature.</param>
    /// <returns>The MQTT topic.</returns>
    public static string ProblemAttributesTopic(BotStatusFeature feature)
        => $"{TopicPrefix}/feature/{feature.Slug}/problem/attrs";

    /// <summary>State topic of the bot status sensor (uptime in seconds).</summary>
    public static string StatusStateTopic => TopicPrefix + "/status/state";

    /// <summary>Topic carrying the JSON attributes of the bot status sensor.</summary>
    public static string StatusAttributesTopic => TopicPrefix + "/status/attrs";

    /// <summary>MQTT discovery configuration topic of an entity.</summary>
    /// <param name="component">The Home Assistant component (sensor, binary_sensor, …).</param>
    /// <param name="entityId">The entity identifier inside the discovery node.</param>
    /// <returns>The discovery configuration topic.</returns>
    public static string DiscoveryTopic(string component, string entityId)
        => $"homeassistant/{component}/{DeviceId}/{entityId}/config";

    /// <summary>
    /// Builds the discovery payload for the bot status sensor: an uptime duration whose attributes
    /// carry version, start time, Discord connection and feature counters.
    /// </summary>
    /// <param name="softwareVersion">The version reported as device software version.</param>
    /// <returns>The serialized MQTT discovery configuration.</returns>
    public static string StatusDiscoveryConfig(string softwareVersion) => JsonSerializer.Serialize(new
    {
        name = "Bot Status",
        object_id = $"{DeviceId}_status",
        unique_id = $"{DeviceId}_status",
        state_topic = StatusStateTopic,
        json_attributes_topic = StatusAttributesTopic,
        device_class = "duration",
        state_class = "measurement",
        unit_of_measurement = "s",
        expire_after = StatusExpireAfterSeconds,
        availability = BuildAvailability(),
        device = BuildDevice(softwareVersion)
    });

    /// <summary>
    /// Builds the discovery payload for a feature's enabled sensor (ON while the feature flag is set).
    /// </summary>
    /// <param name="feature">The feature.</param>
    /// <param name="softwareVersion">The version reported as device software version.</param>
    /// <returns>The serialized MQTT discovery configuration.</returns>
    public static string EnabledDiscoveryConfig(BotStatusFeature feature, string softwareVersion) => JsonSerializer.Serialize(new
    {
        name = $"{feature.DisplayName} Enabled",
        object_id = $"{DeviceId}_{feature.Slug}_enabled",
        unique_id = $"{DeviceId}_{feature.Slug}_enabled",
        state_topic = EnabledStateTopic(feature),
        payload_on = "ON",
        payload_off = "OFF",
        availability = BuildAvailability(),
        device = BuildDevice(softwareVersion)
    });

    /// <summary>
    /// Builds the discovery payload for a feature's problem sensor: ON while the feature's most
    /// recent recorded outcome was a failure. The <c>problem</c> device class makes Home Assistant
    /// show the entity as an active problem, which is what automations trigger on.
    /// </summary>
    /// <param name="feature">The feature.</param>
    /// <param name="softwareVersion">The version reported as device software version.</param>
    /// <returns>The serialized MQTT discovery configuration.</returns>
    public static string ProblemDiscoveryConfig(BotStatusFeature feature, string softwareVersion) => JsonSerializer.Serialize(new
    {
        name = $"{feature.DisplayName} Problem",
        object_id = $"{DeviceId}_{feature.Slug}_problem",
        unique_id = $"{DeviceId}_{feature.Slug}_problem",
        state_topic = ProblemStateTopic(feature),
        json_attributes_topic = ProblemAttributesTopic(feature),
        device_class = "problem",
        payload_on = "ON",
        payload_off = "OFF",
        expire_after = ProblemExpireAfterSeconds,
        availability = BuildAvailability(),
        device = BuildDevice(softwareVersion)
    });

    /// <summary>
    /// Builds the state payload of a feature's enabled sensor.
    /// </summary>
    /// <param name="enabled">Whether the feature flag is set.</param>
    /// <returns>The payload to publish.</returns>
    public static string EnabledStatePayload(bool enabled) => enabled ? "ON" : "OFF";

    /// <summary>
    /// Builds the state payload of a feature's problem sensor.
    /// </summary>
    /// <param name="snapshot">The feature's health snapshot.</param>
    /// <returns>The payload to publish.</returns>
    public static string ProblemStatePayload(FeatureHealthSnapshot snapshot)
        => snapshot.IsProblem ? "ON" : "OFF";

    /// <summary>
    /// Builds the attribute payload of a feature's problem sensor.
    /// </summary>
    /// <param name="snapshot">The feature's health snapshot.</param>
    /// <returns>The serialized JSON attributes.</returns>
    public static string ProblemAttributesPayload(FeatureHealthSnapshot snapshot) => JsonSerializer.Serialize(new
    {
        feature = snapshot.Feature,
        last_success_utc = FormatTimestamp(snapshot.LastSuccessUtc),
        last_failure_utc = FormatTimestamp(snapshot.LastFailureUtc),
        last_error = snapshot.LastError
    });

    /// <summary>
    /// Builds the state payload of the bot status sensor.
    /// </summary>
    /// <param name="uptime">The process uptime.</param>
    /// <returns>The uptime in whole seconds.</returns>
    public static string StatusStatePayload(TimeSpan uptime)
        => ((long)(uptime < TimeSpan.Zero ? TimeSpan.Zero : uptime).TotalSeconds).ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// Builds the attribute payload of the bot status sensor.
    /// </summary>
    /// <param name="attributes">The attribute values to publish.</param>
    /// <returns>The serialized JSON attributes.</returns>
    public static string StatusAttributesPayload(BotStatusAttributes attributes) => JsonSerializer.Serialize(new
    {
        version = attributes.Version,
        started_at_utc = FormatTimestamp(attributes.StartedAtUtc),
        uptime_seconds = attributes.UptimeSeconds,
        discord_connection = attributes.DiscordConnection,
        guilds = attributes.Guilds,
        features_total = attributes.FeaturesTotal,
        features_problem = attributes.FeaturesProblem
    });

    private static object BuildAvailability() => new
    {
        topic = AvailabilityTopic,
        payload_available = AvailabilityOnlinePayload,
        payload_not_available = AvailabilityLostPayload
    };

    private static object BuildDevice(string softwareVersion) => new
    {
        identifiers = new[] { DeviceId },
        name = DeviceName,
        model = "C# Discord Integration",
        manufacturer = "Custom Application",
        sw_version = softwareVersion
    };

    private static string? FormatTimestamp(DateTime? value)
        => value.HasValue ? value.Value.ToString("o", System.Globalization.CultureInfo.InvariantCulture) : null;
}
