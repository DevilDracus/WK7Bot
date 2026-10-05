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

    // Primary free model and fallback model
    private const string PrimaryModel = "gemini-3.8-flash";
    private const string FallbackModel = "gemini-3.1-flash-lite";

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
                       $"IMPORTANT: Use the GERMAN names for fruits, vegetables, herbs and nuts!";

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
    /// <returns>A structured <see cref="RenalRecipeData"/> instance containing diet tags and instructions, or null if processing fails.</returns>
    public async Task<RenalRecipeData?> GetWeeklyRenalRecipeAsync(DateTime dateTime, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.GeminiApiKey))
        {
            _logger.LogWarning("Gemini API key is missing. Skipping renal recipe generation.");
            return null;
        }

        string monthName = dateTime.ToString("MMMM");
        string prompt = $"Create a delicious recipe for the month of {monthName} using local Central European seasonal produce (Germany/Leipzig region), featuring at least one seasonal fruit or vegetable. " +
                        $"CRITICAL DIETARY RESTRICTIONS:\n" +
                        $"1. Tailor the recipe for individuals on dialysis or kidney transplant recipients taking immunosuppressants.\n" +
                        $"2. Keep Potassium (Kalium) moderate/controlled, Phosphorus as low as practical, and Sodium under strict limits.\n" +
                        $"3. Strictly EXCLUDE: any raw or undercooked ingredients, raw sprouts, grapefruit, pomegranate, star fruit (Karambole), unpasteurized ingredients, AND ALL MOLD CHEESES (Schimmelkäse like Gorgonzola, Roquefort, Brie, Camembert are an absolute NO-GO).\n" +
                        $"4. Rohmilchkäse (raw milk cheese) is strictly prohibited unless it is thoroughly heated, cooked in a boiling sauce, or fully baked.\n" +
                        $"5. Every ingredient must be fully cooked (boiled, baked, or fried through). No raw preparations: no raw salads and no fresh-fruit dishes — use fruits cooked instead (e.g., compote, stewed, or baked).\n" +
                        $"6. Prefer low-potassium ingredients and avoid obvious high-potassium items where possible (e.g., banana, potato, tomato paste, dried fruit, nuts). Even better: use preparation methods that leach potassium out of the ingredients (e.g., soak vegetables and boil them in plenty of water, then discard the water) and include those extra steps in the instructions.\n" +
                        $"OUTPUT REQUIREMENTS:\n" +
                        $"7. Generate exactly 4 servings and assign every applicable diet tag from this fixed list, using the exact English identifiers: low_potassium, low_phosphate, low_sodium, low_carb, protein_rich, high_fiber (omit tags that do not apply).\n" +
                        $"8. In transplant_safety_notes, briefly explain why the dish fits these restrictions and add a short disclaimer to consult one's doctor or dietitian.\n\n" +
                        $"IMPORTANT: Write ALL text fields (title, description, prep_time, cook_time, seasonal_ingredients_used, ingredients, instructions, transplant_safety_notes) in GERMAN! Keep diet_tags as the exact English identifiers from the list above.";

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
                ["diet_tags"] = new JsonObject
                {
                    ["type"] = "ARRAY",
                    ["items"] = new JsonObject
                    {
                        ["type"] = "STRING",
                        ["enum"] = new JsonArray
                        {
                            "low_potassium",
                            "low_phosphate",
                            "low_sodium",
                            "low_carb",
                            "protein_rich",
                            "high_fiber"
                        }
                    }
                }
            },
            ["required"] = new JsonArray { "title", "description", "prep_time", "cook_time", "servings", "seasonal_ingredients_used", "ingredients", "instructions", "transplant_safety_notes", "diet_tags" }
        };

        return await RequestGeminiStructuredOutputAsync<RenalRecipeData>(prompt, jsonSchema, cancellationToken);
    }

    /// <summary>
    /// Submits a structured JSON generation payload to Gemini with automatic model fallback and retries on 503/429 errors.
    /// </summary>
    private async Task<T?> RequestGeminiStructuredOutputAsync<T>(string prompt, JsonObject responseSchema, CancellationToken cancellationToken)
    {
        // Try Primary Model first, then fall back to FallbackModel if unavailable
        string[] candidateModels = { PrimaryModel, FallbackModel };

        foreach (var modelName in candidateModels)
        {
            var result = await TryExecuteRequestWithRetryAsync<T>(modelName, prompt, responseSchema, cancellationToken);
            if (result != null)
            {
                return result;
            }

            _logger.LogWarning("Model {ModelName} failed or was unavailable. Trying next candidate...", modelName);
        }

        _logger.LogError("All candidate Gemini models failed to yield a response.");
        return default;
    }

    /// <summary>
    /// Executes the HTTP REST API call to Gemini with exponential backoff for transient 503 / 429 status codes.
    /// </summary>
    private async Task<T?> TryExecuteRequestWithRetryAsync<T>(string modelName, string prompt, JsonObject responseSchema, CancellationToken cancellationToken)
    {
        int maxRetries = 3;
        int delayMs = 2000;

        string requestUrl = $"https://generativelanguage.googleapis.com/v1beta/models/{modelName}:generateContent?key={_options.GeminiApiKey}";

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
                responseSchema = responseSchema,
                maxOutputTokens = 8192 // Ensures large structured recipes finish generating completely without truncation
            }
        };

        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                string jsonPayload = JsonSerializer.Serialize(requestPayload);
                using var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

                using var response = await _httpClient.PostAsync(requestUrl, content, cancellationToken);

                if (response.IsSuccessStatusCode)
                {
                    string responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
                    using var doc = JsonDocument.Parse(responseJson);

                    var root = doc.RootElement;
                    if (root.TryGetProperty("candidates", out var candidates) && candidates.GetArrayLength() > 0)
                    {
                        string innerJson = candidates[0]
                            .GetProperty("content")
                            .GetProperty("parts")[0]
                            .GetProperty("text")
                            .GetString() ?? string.Empty;

                        return JsonSerializer.Deserialize<T>(innerJson);
                    }
                }

                int statusCode = (int)response.StatusCode;
                string errorBody = await response.Content.ReadAsStringAsync(cancellationToken);

                // Retry on transient 503 (Unavailable) or 429 (Too Many Requests)
                if ((statusCode == 503 || statusCode == 429) && attempt < maxRetries)
                {
                    _logger.LogWarning("Gemini API returned status {StatusCode} for model {Model}. Retrying attempt {Attempt}/{Max} in {Delay}ms...",
                        statusCode, modelName, attempt, maxRetries, delayMs);

                    await Task.Delay(delayMs, cancellationToken);
                    delayMs *= 2; // Exponential backoff
                    continue;
                }

                _logger.LogError("Gemini API call to model {Model} failed with status code {StatusCode}: {ErrorBody}",
                    modelName, response.StatusCode, errorBody);
                break;
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                _logger.LogError(ex, "Unexpected exception during Gemini request for model {Model} on attempt {Attempt}", modelName, attempt);
            }
        }

        return default;
    }
}