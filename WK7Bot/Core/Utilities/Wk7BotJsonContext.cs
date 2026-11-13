namespace WK7Bot.Core.Utilities;

using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using WK7Bot.Models;

/// <summary>
/// Source-generated JSON serializer context for the DTOs serialized across the bot's services and models.
/// </summary>
[JsonSerializable(typeof(JsonObject))]
[JsonSerializable(typeof(JsonArray))]
[JsonSerializable(typeof(JsonNode))]
[JsonSerializable(typeof(JsonValue))]
[JsonSerializable(typeof(JsonElement))]
[JsonSerializable(typeof(bool))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(int))]
[JsonSerializable(typeof(long))]
[JsonSerializable(typeof(double))]
[JsonSerializable(typeof(uint))]
[JsonSerializable(typeof(ulong))]
[JsonSerializable(typeof(decimal))]
[JsonSerializable(typeof(PollenForecast))]
[JsonSerializable(typeof(PollenTypeForecast))]
[JsonSerializable(typeof(PollenLevel))]
[JsonSerializable(typeof(UvForecast))]
[JsonSerializable(typeof(SteamWishlistItem))]
[JsonSerializable(typeof(SteamWishlistSub))]
[JsonSerializable(typeof(SteamWishlistResponse))]
[JsonSerializable(typeof(SteamWishlistApiResponse))]
[JsonSerializable(typeof(SteamWishlistApiResponseData))]
[JsonSerializable(typeof(Dictionary<string, SteamWishlistItem>))]
[JsonSerializable(typeof(OpenFoodFactsProduct))]
[JsonSerializable(typeof(List<OpenFoodFactsProduct>))]
[JsonSerializable(typeof(UserPresenceEntity))]
[JsonSerializable(typeof(SteamUserData))]
[JsonSerializable(typeof(BattleNetUserData))]
[JsonSerializable(typeof(DiscordPresenceDiscoveryPayload))]
[JsonSerializable(typeof(DiscordPresenceStatePayload))]
[JsonSerializable(typeof(HomeAssistantNotificationDiscoveryPayload))]
[JsonSerializable(typeof(AlexaNotificationPayload))]
[JsonSerializable(typeof(AddOnOptionsPayload))]
[JsonSerializable(typeof(BotStatusDevice))]
internal partial class Wk7BotJsonContext : JsonSerializerContext
{
}

/// <summary>Home Assistant discovery payload for a Discord presence entity.</summary>
internal sealed class DiscordPresenceDiscoveryPayload
{
    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
    [JsonPropertyName("unique_id")] public string UniqueId { get; set; } = string.Empty;
    [JsonPropertyName("state_topic")] public string StateTopic { get; set; } = string.Empty;
    [JsonPropertyName("value_template")] public string ValueTemplate { get; set; } = string.Empty;
    [JsonPropertyName("json_attributes_topic")] public string JsonAttributesTopic { get; set; } = string.Empty;
    [JsonPropertyName("icon")] public string Icon { get; set; } = string.Empty;
    [JsonPropertyName("device")] public BotStatusDevice Device { get; set; } = new();
}

/// <summary>Home Assistant notification discovery payload.</summary>
internal sealed class HomeAssistantNotificationDiscoveryPayload
{
    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
    [JsonPropertyName("unique_id")] public string UniqueId { get; set; } = string.Empty;
    [JsonPropertyName("command_topic")] public string CommandTopic { get; set; } = string.Empty;
    [JsonPropertyName("device")] public BotStatusDevice Device { get; set; } = new();
}

/// <summary>Presence state payload published to MQTT.</summary>
internal sealed class DiscordPresenceStatePayload
{
    public string Username { get; set; } = string.Empty;
    public ulong DiscordUserId { get; set; }
    public string Status { get; set; } = "Offline";
    public string AvatarUrl { get; set; } = string.Empty;
    public string? GameName { get; set; }
    public string? GameDetails { get; set; }
    public string? GameThumbnailUrl { get; set; }
    public string? CurrentGameTitle { get; set; }
    public uint? CurrentGameAppId { get; set; }
    public int? PlaytimeLastTwoWeeksMinutes { get; set; }
    public string? PlaytimeDisplay { get; set; }
    public string? PersonaState { get; set; }
    public string? SteamAvatarUrl { get; set; }
    public SteamUserData? SteamData { get; set; }
    public string? BattleTag { get; set; }
    public string? BattleNetAvatarUrl { get; set; }
    public string? WoWMainCharacter { get; set; }
    public BattleNetUserData? BattleNetData { get; set; }
    public DateTimeOffset LastUpdated { get; set; } = DateTimeOffset.UtcNow;
    [JsonPropertyName("entity_picture")] public string EntityPicture { get; set; } = string.Empty;
}

/// <summary>Alexa notification payload.</summary>
internal sealed class AlexaNotificationPayload
{
    [JsonPropertyName("message")] public string Message { get; set; } = string.Empty;
}

/// <summary>Add-on options write payload.</summary>
internal sealed class AddOnOptionsPayload
{
    [JsonPropertyName("options")] public JsonObject Options { get; set; } = new();
}