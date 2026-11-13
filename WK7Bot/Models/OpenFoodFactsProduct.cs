namespace WK7Bot.Models;

using System.Text.Json;
using System.Text.Json.Serialization;
using WK7Bot.Core.Utilities;

/// <summary>
/// Represents a single product returned by the Open Food Facts search API.
/// </summary>
public sealed class OpenFoodFactsProduct
{
    /// <summary>The product name in the query language.</summary>
    [JsonPropertyName("product_name")]
    public string ProductName { get; set; } = string.Empty;

    /// <summary>The brand names, comma-separated.</summary>
    [JsonPropertyName("brands")]
    public string Brands { get; set; } = string.Empty;

    /// <summary>The quantity string (e.g. "500 g").</summary>
    [JsonPropertyName("quantity")]
    public string Quantity { get; set; } = string.Empty;

    /// <summary>The Nutri-Score grade (a, b, c, d, e).</summary>
    [JsonPropertyName("nutriscore_grade")]
    public string? NutriScoreGrade { get; set; }

    /// <summary>The NOVA processing group (1-4).</summary>
    [JsonPropertyName("nova_group")]
    public int? NovaGroup { get; set; }

    /// <summary>The URL of the product page on Open Food Facts.</summary>
    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;

    /// <summary>The URL of the product image (front).</summary>
    [JsonPropertyName("image_url")]
    public string? ImageUrl { get; set; }

    /// <summary>The nutritional values per 100g.</summary>
    [JsonPropertyName("nutriments")]
    public OpenFoodFactsNutriments? Nutriments { get; set; }
}

/// <summary>
/// Nutritional values per 100g as returned by the Open Food Facts API.
/// </summary>
public sealed class OpenFoodFactsNutriments
{
    /// <summary>Energy in kcal per 100g.</summary>
    [JsonPropertyName("energy-kcal_100g")]
    public double? EnergyKcal { get; set; }

    /// <summary>Proteins in grams per 100g.</summary>
    [JsonPropertyName("proteins_100g")]
    public double? Proteins { get; set; }

    /// <summary>Carbohydrates in grams per 100g.</summary>
    [JsonPropertyName("carbohydrates_100g")]
    public double? Carbohydrates { get; set; }

    /// <summary>Sugars in grams per 100g.</summary>
    [JsonPropertyName("sugars_100g")]
    public double? Sugars { get; set; }

    /// <summary>Fat in grams per 100g.</summary>
    [JsonPropertyName("fat_100g")]
    public double? Fat { get; set; }

    /// <summary>Saturated fat in grams per 100g.</summary>
    [JsonPropertyName("saturated-fat_100g")]
    public double? SaturatedFat { get; set; }

    /// <summary>Salt in grams per 100g.</summary>
    [JsonPropertyName("salt_100g")]
    public double? Salt { get; set; }

    /// <summary>Sodium in grams per 100g.</summary>
    [JsonPropertyName("sodium_100g")]
    public double? Sodium { get; set; }

    /// <summary>Potassium in grams per 100g.</summary>
    [JsonPropertyName("potassium_100g")]
    public double? Potassium { get; set; }

    /// <summary>Phosphorus in grams per 100g.</summary>
    [JsonPropertyName("phosphorus_100g")]
    public double? Phosphorus { get; set; }

    /// <summary>Fiber in grams per 100g.</summary>
    [JsonPropertyName("fiber_100g")]
    public double? Fiber { get; set; }
}

/// <summary>
/// Root response envelope for the Open Food Facts search API.
/// </summary>
public sealed class OpenFoodFactsSearchResponse
{
    /// <summary>The total number of products matching the query.</summary>
    [JsonPropertyName("count")]
    public int Count { get; set; }

    /// <summary>The page of results.</summary>
    [JsonPropertyName("page")]
    public int Page { get; set; }

    /// <summary>The products on this page.</summary>
    [JsonPropertyName("products")]
    [JsonConverter(typeof(OpenFoodFactsProductsJsonConverter))]
    public List<OpenFoodFactsProduct> Products { get; set; } = new();
}

/// <summary>
/// JSON converter that accepts both array and dictionary (object) forms for the
/// Open Food Facts <c>products</c> collection, normalizing them to a list.
/// </summary>
public sealed class OpenFoodFactsProductsJsonConverter : JsonConverter<List<OpenFoodFactsProduct>>
{
    /// <inheritdoc />
    public override List<OpenFoodFactsProduct> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        List<OpenFoodFactsProduct> result = [];

        if (reader.TokenType == JsonTokenType.Null)
        {
            return result;
        }

        if (reader.TokenType == JsonTokenType.StartArray)
        {
            while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
            {
                if (reader.TokenType == JsonTokenType.StartObject)
                {
                    var product = JsonSerializer.Deserialize<OpenFoodFactsProduct>(ref reader, options);
                    if (product is not null)
                    {
                        result.Add(product);
                    }
                }
                else
                {
                    reader.Skip();
                }
            }

            return result;
        }

        if (reader.TokenType == JsonTokenType.StartObject)
        {
            using var document = JsonDocument.ParseValue(ref reader);
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (property.Value.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var product = JsonSerializer.Deserialize<OpenFoodFactsProduct>(property.Value.GetRawText(), options);
                if (product is not null)
                {
                    result.Add(product);
                }
            }

            return result;
        }

        reader.Skip();
        return result;
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, List<OpenFoodFactsProduct> value, JsonSerializerOptions options)
    {
        var context = new Wk7BotJsonContext(options);
        JsonSerializer.Serialize(writer, value, context.GetTypeInfo(typeof(List<OpenFoodFactsProduct>))!);
    }
}
