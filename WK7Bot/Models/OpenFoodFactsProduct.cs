namespace WK7Bot.Models;

using System.Text.Json.Serialization;

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
    public OpenFoodFactsNutriments Nutriments { get; set; } = new();
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
    public List<OpenFoodFactsProduct> Products { get; set; } = new();
}
