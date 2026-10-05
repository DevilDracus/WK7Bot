namespace WK7Bot.Models;

using System.Collections.Generic;
using System.Text.Json.Serialization;

/// <summary>
/// Container model representing seasonal regional produce returned by Gemini structured outputs.
/// </summary>
public class SeasonalFoodData
{
    /// <summary>
    /// Gets or sets the target month name.
    /// </summary>
    [JsonPropertyName("month")]
    public string Month { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the collection of seasonal fruits.
    /// </summary>
    [JsonPropertyName("fruits")]
    public List<string> Fruits { get; set; } = new();

    /// <summary>
    /// Gets or sets the collection of seasonal vegetables.
    /// </summary>
    [JsonPropertyName("vegetables")]
    public List<string> Vegetables { get; set; } = new();

    /// <summary>
    /// Gets or sets the collection of seasonal herbs.
    /// </summary>
    [JsonPropertyName("herbs")]
    public List<string> Herbs { get; set; } = new();

    /// <summary>
    /// Gets or sets the collection of seasonal nuts.
    /// </summary>
    [JsonPropertyName("nuts")]
    public List<string> Nuts { get; set; } = new();
}

/// <summary>
/// Detailed nutritional breakdown including target micronutrients.
/// </summary>
public class RecipeNutrition
{
    /// <summary>
    /// Gets or sets total energy in kilocalories.
    /// </summary>
    [JsonPropertyName("calories_kcal")]
    public int CaloriesKcal { get; set; }

    /// <summary>
    /// Gets or sets total protein content in grams.
    /// </summary>
    [JsonPropertyName("protein_g")]
    public double ProteinGrams { get; set; }

    /// <summary>
    /// Gets or sets total carbohydrate content in grams.
    /// </summary>
    [JsonPropertyName("carbohydrates_g")]
    public double CarbohydratesGrams { get; set; }

    /// <summary>
    /// Gets or sets total fat content in grams.
    /// </summary>
    [JsonPropertyName("fat_g")]
    public double FatGrams { get; set; }

    /// <summary>
    /// Gets or sets sodium content in milligrams.
    /// </summary>
    [JsonPropertyName("sodium_mg")]
    public double SodiumMg { get; set; }

    /// <summary>
    /// Gets or sets potassium (Kalium) content in milligrams.
    /// </summary>
    [JsonPropertyName("potassium_kalium_mg")]
    public double PotassiumKaliumMg { get; set; }

    /// <summary>
    /// Gets or sets sulfate content in milligrams.
    /// </summary>
    [JsonPropertyName("sulfate_mg")]
    public double SulfateMg { get; set; }

    /// <summary>
    /// Gets or sets phosphorus content in milligrams.
    /// </summary>
    [JsonPropertyName("phosphorus_mg")]
    public double PhosphorusMg { get; set; }
}

/// <summary>
/// Dialysis and immunosuppressive friendly weekly recipe structure generated via Gemini API.
/// </summary>
public class RenalRecipeData
{
    /// <summary>
    /// Gets or sets the recipe title.
    /// </summary>
    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a short summary highlighting seasonal focus and renal safety details.
    /// </summary>
    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets prep time string (e.g. "20 mins").
    /// </summary>
    [JsonPropertyName("prep_time")]
    public string PrepTime { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets cook time string (e.g. "30 mins").
    /// </summary>
    [JsonPropertyName("cook_time")]
    public string CookTime { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets serving size count.
    /// </summary>
    [JsonPropertyName("servings")]
    public int Servings { get; set; }

    /// <summary>
    /// Gets or sets list of seasonal ingredients utilized.
    /// </summary>
    [JsonPropertyName("seasonal_ingredients_used")]
    public List<string> SeasonalIngredientsUsed { get; set; } = new();

    /// <summary>
    /// Gets or sets itemized recipe ingredients with measures.
    /// </summary>
    [JsonPropertyName("ingredients")]
    public List<string> Ingredients { get; set; } = new();

    /// <summary>
    /// Gets or sets step-by-step cooking instructions.
    /// </summary>
    [JsonPropertyName("instructions")]
    public List<string> Instructions { get; set; } = new();

    /// <summary>
    /// Gets or sets complete nutritional breakdown per serving.
    /// </summary>
    [JsonPropertyName("nutrition_per_serving")]
    public RecipeNutrition Nutrition { get; set; } = new();

    /// <summary>
    /// Gets or sets crucial food safety and medical diet notes (e.g., thorough cooking, grapefruit/pomegranate avoidance).
    /// </summary>
    [JsonPropertyName("transplant_safety_notes")]
    public string TransplantSafetyNotes { get; set; } = string.Empty;
}