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
    /// Gets or sets the qualitative diet tags (e.g., <c>low_potassium</c>, <c>protein_rich</c>) assigned by Gemini.
    /// </summary>
    [JsonPropertyName("diet_tags")]
    public List<string> DietTags { get; set; } = new();

    /// <summary>
    /// Gets or sets crucial food safety and medical diet notes (e.g., thorough cooking, grapefruit/pomegranate avoidance).
    /// </summary>
    [JsonPropertyName("transplant_safety_notes")]
    public string TransplantSafetyNotes { get; set; } = string.Empty;
}