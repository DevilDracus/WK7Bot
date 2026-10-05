namespace WK7Bot.Services;

using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WK7Bot.Models;
using WK7Bot.Options;
using WK7Bot.Services.Interfaces;

/// <summary>
/// Communicates with Google Gemini REST API endpoints using structured JSON schemas to retrieve seasonal produce and dialysis-safe recipes.
/// </summary>
public class GeminiFoodService : IGeminiFoodService
{
    private readonly HttpClient _httpClient;
    private readonly Wk7BotOptions _options;
    private readonly ILogger<GeminiFoodService> _logger;

    private const string GeminiModel = "gemini-3.8-flash";

    /// <summary>
    /// Initializes a new instance of the <see cref="GeminiFoodService"/> class.
    /// </summary>
    /// <param name="httpClient">The HTTP client instance.</param>
    /// <param name="options">The strongly-typed options instance containing configuration parameters.</param>
    /// <param name="logger">The logger instance.</param>
    /// <exception cref="ArgumentNullException">Thrown when required dependencies are null.</exception>
    public GeminiFoodService(
        HttpClient httpClient,
        IOptions<Wk7BotOptions> options,
        ILogger<GeminiFoodService> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    }

    /// <summary>
    /// Queries the Gemini API for seasonal fruits, vegetables, herbs, and nuts for a given month and region.
    /// </summary>
    /// <param name="dateTime">The target month date instance.</param>
    /// <param name="cancellationToken">A token to monitor for task cancellation.</param>
    /// <returns>A structured <see cref="SeasonalFoodData"/> instance or null if processing fails.</returns>
    public async Task<SeasonalFoodData?> GetSeasonalProduceAsync(DateTime dateTime, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.GeminiApiKey))
        {
            _logger.LogWarning("Gemini API key is missing. Skipping seasonal produce generation.");
            return null;
        }

        string monthName = dateTime.ToString("MMMM");
        string prompt = $"Provide a complete list of seasonal produce for the month of {monthName} in Central Europe (Germany/Leipzig region). " +
                       $"Categorize every item strictly into fruits, vegetables, herbs, and nuts.\n" +
                       $"IMPORTANT: Use the GERMAN names of the fruits, vegetables, herbs and nuts!";

        var jsonSchema = new JsonObject
        {
            ["type"] = "OBJECT",
            ["properties"] = new JsonObject
            {
                ["month"] = new JsonObject { ["type"] = "STRING" },
                ["fruits"] = new JsonObject { ["type"] = "ARRAY", ["items"] = new JsonObject { ["type"] = "STRING" } },
                ["vegetables"] = new JsonObject { ["type"] = "ARRAY", ["items"] = new JsonObject { ["type"] = "STRING" } },
                ["herbs"] = new JsonObject { ["type"] = "ARRAY", ["items"] = new JsonObject { ["type"] = "STRING" } },
                ["nuts"] = new JsonObject { ["type"] = "ARRAY", ["items"] = new JsonObject { ["type"] = "STRING" } }
            },
            ["required"] = new JsonArray { "month", "fruits", "vegetables", "herbs", "nuts" }
        };

        return await RequestGeminiStructuredOutputAsync<SeasonalFoodData>(prompt, jsonSchema, cancellationToken);
    }

    /// <summary>
    /// Queries the Gemini API to generate a dialysis and immunosuppressive recipient-friendly recipe using current seasonal produce.
    /// </summary>
    /// <param name="dateTime">The target date used to determine seasonal ingredients.</param>
    /// <param name="cancellationToken">A token to monitor for task cancellation.</param>
    /// <returns>A structured <see cref="RenalRecipeData"/> instance containing nutritional metrics and instructions, or null if processing fails.</returns>
    public async Task<RenalRecipeData?> GetWeeklyRenalRecipeAsync(DateTime dateTime, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.GeminiApiKey))
        {
            _logger.LogWarning("Gemini API key is missing. Skipping renal recipe generation.");
            return null;
        }

        string monthName = dateTime.ToString("MMMM");
        string prompt = $"Create a delicious recipe for {monthName} using local Central European seasonal fruits or vegetables. " +
                       $"CRITICAL DIETARY RESTRICTIONS:\n" +
                       $"1. Tailor for individuals on dialysis or kidney transplant recipients taking immunosuppressants.\n" +
                       $"2. Keep Potassium (Kalium) moderate/controlled and Sodium under strict limits.\n" +
                       $"3. Strictly EXCLUDE raw/undercooked items, raw sprouts, grapefruit, pomegranate, and unpasteurized ingredients.\n" +
                       $"4. Ensure ALL ingredients are fully cooked to prevent foodborne illness.\n" +
                       $"5. Compute full nutritional values per serving including Calories, Protein, Carbohydrates, Fat, Sodium, Potassium (Kalium), Sulfate, and Phosphorus.\n\n" +
                       $"IMPORTANT: Use GERMAN for the recipe!";

        var jsonSchema = new JsonObject
        {
            ["type"] = "OBJECT",
            ["properties"] = new JsonObject
            {
                ["title"] = new JsonObject { ["type"] = "STRING" },
                ["description"] = new JsonObject { ["type"] = "STRING" },
                ["prep_time"] = new JsonObject { ["type"] = "STRING" },
                ["cook_time"] = new JsonObject { ["type"] = "STRING" },
                ["servings"] = new JsonObject { ["type"] = "INTEGER" },
                ["seasonal_ingredients_used"] = new JsonObject { ["type"] = "ARRAY", ["items"] = new JsonObject { ["type"] = "STRING" } },
                ["ingredients"] = new JsonObject { ["type"] = "ARRAY", ["items"] = new JsonObject { ["type"] = "STRING" } },
                ["instructions"] = new JsonObject { ["type"] = "ARRAY", ["items"] = new JsonObject { ["type"] = "STRING" } },
                ["transplant_safety_notes"] = new JsonObject { ["type"] = "STRING" },
                ["nutrition_per_serving"] = new JsonObject
                {
                    ["type"] = "OBJECT",
                    ["properties"] = new JsonObject
                    {
                        ["calories_kcal"] = new JsonObject { ["type"] = "INTEGER" },
                        ["protein_g"] = new JsonObject { ["type"] = "NUMBER" },
                        ["carbohydrates_g"] = new JsonObject { ["type"] = "NUMBER" },
                        ["fat_g"] = new JsonObject { ["type"] = "NUMBER" },
                        ["sodium_mg"] = new JsonObject { ["type"] = "NUMBER" },
                        ["potassium_kalium_mg"] = new JsonObject { ["type"] = "NUMBER" },
                        ["sulfate_mg"] = new JsonObject { ["type"] = "NUMBER" },
                        ["phosphorus_mg"] = new JsonObject { ["type"] = "NUMBER" }
                    },
                    ["required"] = new JsonArray { "calories_kcal", "protein_g", "carbohydrates_g", "fat_g", "sodium_mg", "potassium_kalium_mg", "sulfate_mg", "phosphorus_mg" }
                }
            },
            ["required"] = new JsonArray { "title", "description", "prep_time", "cook_time", "servings", "seasonal_ingredients_used", "ingredients", "instructions", "transplant_safety_notes", "nutrition_per_serving" }
        };

        return await RequestGeminiStructuredOutputAsync<RenalRecipeData>(prompt, jsonSchema, cancellationToken);
    }

    /// <summary>
    /// Submits a structured JSON generation payload to the Gemini API and deserializes the typed output.
    /// </summary>
    /// <typeparam name="T">The target model type.</typeparam>
    /// <param name="prompt">User prompt text.</param>
    /// <param name="responseSchema">JSON Schema definition node.</param>
    /// <param name="cancellationToken">A token to monitor for operation cancellation.</param>
    /// <returns>Deserialized output model or default value on error.</returns>
    private async Task<T?> RequestGeminiStructuredOutputAsync<T>(string prompt, JsonObject responseSchema, CancellationToken cancellationToken)
    {
        try
        {
            string requestUrl = $"https://generativelanguage.googleapis.com/v1beta/models/{GeminiModel}:generateContent?key={_options.GeminiApiKey}";

            var requestPayload = new
            {
                contents = new[]
                {
                    new
                    {
                        parts = new[]
                        {
                            new { text = prompt }
                        }
                    }
                },
                generationConfig = new
                {
                    responseMimeType = "application/json",
                    responseSchema = responseSchema
                }
            };

            string jsonPayload = JsonSerializer.Serialize(requestPayload);
            using var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

            using var response = await _httpClient.PostAsync(requestUrl, content, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                string errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogError("Gemini API call failed with status code {StatusCode}: {ErrorBody}", response.StatusCode, errorBody);
                return default;
            }

            string responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(responseJson);

            var root = doc.RootElement;
            if (!root.TryGetProperty("candidates", out var candidates) || candidates.GetArrayLength() == 0)
            {
                _logger.LogWarning("Gemini API response contained no candidates.");
                return default;
            }

            var textProperty = candidates[0]
                .GetProperty("content")
                .GetProperty("parts")[0]
                .GetProperty("text");

            string innerJson = textProperty.GetString() ?? string.Empty;
            return JsonSerializer.Deserialize<T>(innerJson);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed executing structured Gemini API generation call.");
            return default;
        }
    }
}