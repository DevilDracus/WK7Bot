namespace WK7Bot.Models;

using System.Text.Json;
using System.Text.Json.Serialization;
using WK7Bot.Core.Utilities;

/// <summary>
/// Represents a single game entry on a Steam user's wishlist.
/// </summary>
public sealed class SteamWishlistItem
{
    /// <summary>The Steam application identifier.</summary>
    [JsonPropertyName("appid")]
    public uint AppId { get; set; }

    /// <summary>The display name of the game.</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>The store sub ID for purchase options.</summary>
    [JsonPropertyName("subs")]
    public List<SteamWishlistSub> Subs { get; set; } = new();
}

/// <summary>
/// Represents a subscription/pricing option on a Steam wishlist item.
/// </summary>
public sealed class SteamWishlistSub
{
    /// <summary>The subscription identifier.</summary>
    [JsonPropertyName("subid")]
    public uint SubId { get; set; }
}

/// <summary>
/// Represents the raw JSON response from the Steam wishlist endpoint.
/// The endpoint returns a flat object where keys are app IDs and values are item objects.
/// </summary>
[JsonConverter(typeof(SteamWishlistResponseJsonConverter))]
public sealed class SteamWishlistResponse : Dictionary<string, SteamWishlistItem>
{
}

/// <summary>
/// Represents the raw JSON response from the IWishlistService/GetWishlist endpoint.
/// The endpoint returns { "response": { "items": [...] } }.
/// </summary>
public sealed class SteamWishlistApiResponse
{
    /// <summary>The response wrapper.</summary>
    [JsonPropertyName("response")]
    public SteamWishlistApiResponseData? Response { get; set; }
}

/// <summary>
/// The inner data of the IWishlistService response.
/// </summary>
public sealed class SteamWishlistApiResponseData
{
    /// <summary>The wishlist items.</summary>
    [JsonPropertyName("items")]
    public List<SteamWishlistItem>? Items { get; set; }
}


/// <summary>
/// Represents current pricing data for a game from IsThereAnyDeal.
/// </summary>
public sealed class ItadPriceData
{
    /// <summary>The game's title.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>The current best price across all tracked stores.</summary>
    public decimal CurrentPrice { get; set; }

    /// <summary>The regular non-sale price.</summary>
    public decimal RegularPrice { get; set; }

    /// <summary>The historical lowest price ever recorded.</summary>
    public decimal HistoricalLow { get; set; }

    /// <summary>The store name offering the current best price.</summary>
    public string StoreName { get; set; } = string.Empty;

    /// <summary>The URL to the deal page.</summary>
    public string DealUrl { get; set; } = string.Empty;

    /// <summary>The discount percentage (0-100).</summary>
    public int DiscountPercent => RegularPrice > 0
        ? (int)((1 - (CurrentPrice / RegularPrice)) * 100)
        : 0;

    /// <summary>Whether the current price matches or beats the historical low.</summary>
    public bool IsHistoricalLow => CurrentPrice <= HistoricalLow;
}

/// <summary>
/// Represents a deal alert that should be posted to Discord.
/// </summary>
public sealed class WishlistDealAlert
{
    /// <summary>The Steam application identifier.</summary>
    public uint AppId { get; set; }

    /// <summary>The game title.</summary>
    public string GameTitle { get; set; } = string.Empty;

    /// <summary>The current best price.</summary>
    public decimal CurrentPrice { get; set; }

    /// <summary>The regular price before discount.</summary>
    public decimal RegularPrice { get; set; }

    /// <summary>The historical lowest price.</summary>
    public decimal HistoricalLow { get; set; }

    /// <summary>The discount percentage.</summary>
    public int DiscountPercent { get; set; }

    /// <summary>Whether this is a historical low alert.</summary>
    public bool IsHistoricalLow { get; set; }

    /// <summary>The store offering the deal.</summary>
    public string StoreName { get; set; } = string.Empty;

    /// <summary>The URL to the deal.</summary>
    public string DealUrl { get; set; } = string.Empty;

    /// <summary>The Discord user IDs who have this game on their wishlist.</summary>
    public List<ulong> NotifiedUserIds { get; set; } = new();
}

/// <summary>
/// JSON converter for <see cref="SteamWishlistResponse" /> that skips null/empty entries
/// and tolerates null/empty payloads.
/// </summary>
public sealed class SteamWishlistResponseJsonConverter : JsonConverter<SteamWishlistResponse>
{
    /// <inheritdoc />
    public override SteamWishlistResponse? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        if (reader.TokenType != JsonTokenType.StartObject)
        {
            reader.Skip();
            return new SteamWishlistResponse();
        }

        var response = new SteamWishlistResponse();
        using var document = JsonDocument.ParseValue(ref reader);
        foreach (var property in document.RootElement.EnumerateObject())
        {
            if (string.IsNullOrWhiteSpace(property.Name))
            {
                continue;
            }

            if (property.Value.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var item = JsonSerializer.Deserialize<SteamWishlistItem>(property.Value.GetRawText(), options);
            if (item is not null)
            {
                response[property.Name] = item;
            }
        }

        return response;
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, SteamWishlistResponse value, JsonSerializerOptions options)
    {
        var context = new Wk7BotJsonContext(options);
        JsonSerializer.Serialize(writer, (IDictionary<string, SteamWishlistItem>)value, context.GetTypeInfo(typeof(Dictionary<string, SteamWishlistItem>))!);
    }
}
