namespace WK7Bot.Core.Utilities;

using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>
/// Source-generated JSON serializer context for the payloads produced by
/// <see cref="BotStatusEntities"/>. Registering every payload shape here avoids
/// reflection-based serialization on the MQTT hot path.
/// </summary>
[JsonSerializable(typeof(BotStatusAvailability))]
[JsonSerializable(typeof(BotStatusDevice))]
[JsonSerializable(typeof(StatusDiscoveryJson))]
[JsonSerializable(typeof(EnabledDiscoveryJson))]
[JsonSerializable(typeof(ProblemDiscoveryJson))]
[JsonSerializable(typeof(ProblemAttributesJson))]
[JsonSerializable(typeof(StatusAttributesJson))]
internal partial class BotStatusJsonContext : JsonSerializerContext
{
}

/// <summary>Payload published on the availability topic entry of a discovery config.</summary>
internal sealed record BotStatusAvailability
{
    [JsonPropertyName("topic")] public string Topic { get; init; } = string.Empty;
    [JsonPropertyName("payload_available")] public string PayloadAvailable { get; init; } = string.Empty;
    [JsonPropertyName("payload_not_available")] public string PayloadNotAvailable { get; init; } = string.Empty;
}

/// <summary>Payload published as the <c>device</c> block of a discovery config.</summary>
internal sealed record BotStatusDevice
{
    [JsonPropertyName("identifiers")] public string[] Identifiers { get; init; } = Array.Empty<string>();
    [JsonPropertyName("name")] public string Name { get; init; } = string.Empty;
    [JsonPropertyName("model")] public string Model { get; init; } = string.Empty;
    [JsonPropertyName("manufacturer")] public string Manufacturer { get; init; } = string.Empty;
    [JsonPropertyName("sw_version")] public string SwVersion { get; init; } = string.Empty;
}

/// <summary>Discovery payload for the bot status sensor.</summary>
internal sealed record StatusDiscoveryJson
{
    [JsonPropertyName("name")] public string Name { get; init; } = string.Empty;
    [JsonPropertyName("object_id")] public string ObjectId { get; init; } = string.Empty;
    [JsonPropertyName("unique_id")] public string UniqueId { get; init; } = string.Empty;
    [JsonPropertyName("state_topic")] public string StateTopic { get; init; } = string.Empty;
    [JsonPropertyName("json_attributes_topic")] public string JsonAttributesTopic { get; init; } = string.Empty;
    [JsonPropertyName("device_class")] public string DeviceClass { get; init; } = string.Empty;
    [JsonPropertyName("state_class")] public string StateClass { get; init; } = string.Empty;
    [JsonPropertyName("unit_of_measurement")] public string UnitOfMeasurement { get; init; } = string.Empty;
    [JsonPropertyName("expire_after")] public int ExpireAfter { get; init; }
    [JsonPropertyName("availability")] public BotStatusAvailability Availability { get; init; } = default!;
    [JsonPropertyName("device")] public BotStatusDevice Device { get; init; } = default!;
}

/// <summary>Discovery payload for a feature's enabled sensor.</summary>
internal sealed record EnabledDiscoveryJson
{
    [JsonPropertyName("name")] public string Name { get; init; } = string.Empty;
    [JsonPropertyName("object_id")] public string ObjectId { get; init; } = string.Empty;
    [JsonPropertyName("unique_id")] public string UniqueId { get; init; } = string.Empty;
    [JsonPropertyName("state_topic")] public string StateTopic { get; init; } = string.Empty;
    [JsonPropertyName("payload_on")] public string PayloadOn { get; init; } = string.Empty;
    [JsonPropertyName("payload_off")] public string PayloadOff { get; init; } = string.Empty;
    [JsonPropertyName("availability")] public BotStatusAvailability Availability { get; init; } = default!;
    [JsonPropertyName("device")] public BotStatusDevice Device { get; init; } = default!;
}

/// <summary>Discovery payload for a feature's problem sensor.</summary>
internal sealed record ProblemDiscoveryJson
{
    [JsonPropertyName("name")] public string Name { get; init; } = string.Empty;
    [JsonPropertyName("object_id")] public string ObjectId { get; init; } = string.Empty;
    [JsonPropertyName("unique_id")] public string UniqueId { get; init; } = string.Empty;
    [JsonPropertyName("state_topic")] public string StateTopic { get; init; } = string.Empty;
    [JsonPropertyName("json_attributes_topic")] public string JsonAttributesTopic { get; init; } = string.Empty;
    [JsonPropertyName("device_class")] public string DeviceClass { get; init; } = string.Empty;
    [JsonPropertyName("payload_on")] public string PayloadOn { get; init; } = string.Empty;
    [JsonPropertyName("payload_off")] public string PayloadOff { get; init; } = string.Empty;
    [JsonPropertyName("expire_after")] public int ExpireAfter { get; init; }
    [JsonPropertyName("availability")] public BotStatusAvailability Availability { get; init; } = default!;
    [JsonPropertyName("device")] public BotStatusDevice Device { get; init; } = default!;
}

/// <summary>Attribute payload for a feature's problem sensor.</summary>
internal sealed record ProblemAttributesJson
{
    [JsonPropertyName("feature")] public string Feature { get; init; } = string.Empty;
    [JsonPropertyName("last_success_utc")] public string? LastSuccessUtc { get; init; }
    [JsonPropertyName("last_failure_utc")] public string? LastFailureUtc { get; init; }
    [JsonPropertyName("last_error")] public string? LastError { get; init; }
}

/// <summary>Attribute payload for the bot status sensor.</summary>
internal sealed record StatusAttributesJson
{
    [JsonPropertyName("version")] public string Version { get; init; } = string.Empty;
    [JsonPropertyName("started_at_utc")] public string? StartedAtUtc { get; init; }
    [JsonPropertyName("uptime_seconds")] public long UptimeSeconds { get; init; }
    [JsonPropertyName("discord_connection")] public string DiscordConnection { get; init; } = string.Empty;
    [JsonPropertyName("guilds")] public int Guilds { get; init; }
    [JsonPropertyName("features_total")] public int FeaturesTotal { get; init; }
    [JsonPropertyName("features_problem")] public int FeaturesProblem { get; init; }
}