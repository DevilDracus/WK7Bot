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
using WK7Bot.Core.Utilities;
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
    private readonly GeminiCircuitBreaker _circuitBreaker;

    /// <summary>
    /// Primary Gemini model used for structured output generation.
    /// </summary>
    private const string PrimaryModel = "gemini-3.8-flash";

    /// <summary>
    /// Lite fallback model tried when the primary model is unavailable.
    /// </summary>
    private const string FallbackModel = "gemini-3.1-flash-lite";

    /// Delay before the first retry of a transient (429/503) Gemini failure; doubles per attempt.
    /// Virtual so tests can override it to zero instead of sleeping through real backoff.
    /// </summary>
    protected virtual TimeSpan RetryDelay => TimeSpan.FromMilliseconds(2000);

    /// <summary>
    /// Additional delay applied when the server returns 503 Service Unavailable,
    /// on top of the regular exponential backoff.
    /// </summary>
    protected virtual TimeSpan ServiceUnavailableExtraDelay => TimeSpan.FromSeconds(3);

    /// <summary>
    /// Deserializer options for the structured answer: property names arrive from the model, so
    /// matching is case-insensitive to tolerate casing drift without silently dropping all values.
    /// </summary>
    private static readonly JsonSerializerOptions StructuredAnswerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Determines whether an exception represents a transient failure that may succeed on retry.
    /// </summary>
    /// <param name="ex">The exception to inspect.</param>
    /// <returns><see langword="true"/> when the failure is likely transient.</returns>
    private static bool IsTransient(Exception ex)
    {
        return ex is HttpRequestException
            or TimeoutException
            or TaskCanceledException
            or IOException
            or JsonException;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="GeminiFoodService"/> class.
    /// </summary>
    /// <param name="httpClient">The HTTP client instance.</param>
    /// <param name="options">The strongly-typed options instance containing configuration parameters.</param>
    /// <param name="logger">The logger instance.</param>
    /// <param name="circuitBreaker">The circuit breaker for Gemini API calls.</param>
    /// <exception cref="ArgumentNullException">Thrown when required dependencies are null.</exception>
    public GeminiFoodService(
        HttpClient httpClient,
        IOptions<Wk7BotOptions> options,
        ILogger<GeminiFoodService> logger,
        GeminiCircuitBreaker circuitBreaker)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        // Typed HTTP client timeout is configured centrally in ServiceCollectionExtensions.
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _circuitBreaker = circuitBreaker ?? throw new ArgumentNullException(nameof(circuitBreaker));
    }

    /// <summary>
    /// Queries the Gemini API for seasonal fruits, vegetables, herbs, and nuts for a given month and region.
    /// </summary>
    /// <param name="dateTime">The target month date instance.</param>
    /// <param name="cancellationToken">A token to monitor for task cancellation.</param>
    /// <returns>A structured <see cref="SeasonalFoodData"/> instance or null if processing fails.</returns>
    public async Task<SeasonalFoodData?> GetSeasonalProduceAsync(DateTime dateTime, CancellationToken cancellationToken = default)
    {
        if (_circuitBreaker.IsCircuitOpen())
        {
            _logger.LogWarning("Gemini circuit breaker is open; skipping seasonal produce request.");
            return null;
        }

        if (string.IsNullOrWhiteSpace(_options.GeminiApiKey))
        {
            _logger.LogWarning("Gemini API key is missing. Skipping seasonal produce generation.");
            return null;
        }

        string monthName = dateTime.GermanMonthName();
        var (prompt, jsonSchema) = GeminiPromptBuilder.BuildSeasonalProduce(monthName);

        var result = await RequestGeminiStructuredOutputAsync<SeasonalFoodData>(prompt, jsonSchema, cancellationToken);

        if (result != null)
        {
            _circuitBreaker.RecordSuccess();
        }
        else
        {
            _circuitBreaker.RecordFailure();
        }

        return result;
    }

    /// <summary>
    /// Queries the Gemini API to generate a dialysis and immunosuppressive recipient-friendly recipe using current seasonal produce.
    /// </summary>
    /// <param name="dateTime">The target date used to determine seasonal ingredients.</param>
    /// <param name="cancellationToken">A token to monitor for task cancellation.</param>
    /// <returns>A structured <see cref="RenalRecipeData"/> instance containing diet tags and instructions, or null if processing fails.</returns>
    public async Task<RenalRecipeData?> GetWeeklyRenalRecipeAsync(DateTime dateTime, CancellationToken cancellationToken = default)
    {
        if (_circuitBreaker.IsCircuitOpen())
        {
            _logger.LogWarning("Gemini circuit breaker is open; skipping renal recipe request.");
            return null;
        }

        if (string.IsNullOrWhiteSpace(_options.GeminiApiKey))
        {
            _logger.LogWarning("Gemini API key is missing. Skipping renal recipe generation.");
            return null;
        }

        string monthName = dateTime.GermanMonthName();
        var (prompt, jsonSchema) = GeminiPromptBuilder.BuildWeeklyRenalRecipe(monthName);

        var result = await RequestGeminiStructuredOutputAsync<RenalRecipeData>(prompt, jsonSchema, cancellationToken);

        if (result != null)
        {
            _circuitBreaker.RecordSuccess();
        }
        else
        {
            _circuitBreaker.RecordFailure();
        }

        return result;
    }

    /// <summary>
    /// Re-shapes a scraped recipe into the canonical recipe format via Gemini structured output, preserving the
    /// original source link and never adding diet tags or medical notes.
    /// </summary>
    /// <param name="recipe">The parsed recipe to normalise.</param>
    /// <param name="cancellationToken">A token to monitor for task cancellation.</param>
    /// <returns>The formatted recipe, or <see langword="null"/> when the API key is missing, the call fails, or the
    /// structured answer is unusable.</returns>
    public async Task<RenalRecipeData?> FormatRecipeAsync(RenalRecipeData recipe, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(recipe);

        if (string.IsNullOrWhiteSpace(_options.GeminiApiKey))
        {
            _logger.LogWarning("Gemini API key is missing. Skipping recipe formatting.");
            return null;
        }

        var scrapedRecipe = new JsonObject
        {
            ["title"] = recipe.Title,
            ["description"] = recipe.Description,
            ["prep_time"] = recipe.PrepTime,
            ["cook_time"] = recipe.CookTime,
            ["servings"] = recipe.Servings,
            ["ingredients"] = new JsonArray(recipe.Ingredients.Select(i => (JsonNode?)JsonValue.Create(i)).ToArray()),
            ["instructions"] = new JsonArray(recipe.Instructions.Select(i => (JsonNode?)JsonValue.Create(i)).ToArray())
        };

        var (prompt, jsonSchema) = GeminiPromptBuilder.BuildFormatRecipe(scrapedRecipe);

        var formatted = await RequestGeminiStructuredOutputAsync<RenalRecipeData>(prompt, jsonSchema, cancellationToken);

        if (formatted == null)
        {
            _circuitBreaker.RecordFailure();
            return null;
        }

        if (string.IsNullOrWhiteSpace(formatted.Title)
            || formatted.Ingredients is not { Count: > 0 }
            || formatted.Instructions is not { Count: > 0 })
        {
            _circuitBreaker.RecordFailure();
            _logger.LogWarning("Gemini returned an unusable formatted recipe (title/ingredients/steps missing).");
            return null;
        }

        // Formatting must never lose content: if Gemini dropped steps or ingredients, keep the raw scrape instead.
        if (recipe.Instructions.Count > 0 && formatted.Instructions.Count < recipe.Instructions.Count)
        {
            _logger.LogWarning(
                "Gemini kept only {Formatted} of {Original} steps while formatting; posting the raw recipe instead.",
                formatted.Instructions.Count,
                recipe.Instructions.Count);
            return null;
        }

        if (recipe.Ingredients.Count > 0 && formatted.Ingredients.Count < recipe.Ingredients.Count)
        {
            _logger.LogWarning(
                "Gemini kept only {Formatted} of {Original} ingredients while formatting; posting the raw recipe instead.",
                formatted.Ingredients.Count,
                recipe.Ingredients.Count);
            return null;
        }

        // Formatting must never carry provenance away or introduce unverified medical claims.
        formatted = formatted with
        {
            SourceUrl = recipe.SourceUrl,
            ImageUrl = recipe.ImageUrl,
            SeasonalIngredientsUsed = new List<string>(),
            DietTags = new List<string>(),
            TransplantSafetyNotes = string.Empty
        };

        return formatted;
    }

    /// <summary>
    /// Submits a structured JSON generation payload to Gemini with automatic model fallback and retries on 503/429 errors.
    /// </summary>
    private async Task<TResponse?> RequestGeminiStructuredOutputAsync<TResponse>(string prompt, JsonObject jsonSchema, CancellationToken cancellationToken = default) where TResponse : class
    {
        if (_circuitBreaker.IsCircuitOpen())
        {
            return null;
        }
        // Try Primary Model first, then fall back to FallbackModel if unavailable
        string[] candidateModels = { PrimaryModel, FallbackModel };

        foreach (var modelName in candidateModels)
        {
            var result = await TryExecuteRequestWithRetryAsync<TResponse>(modelName, prompt, jsonSchema, cancellationToken);
            if (result != null)
            {
                _circuitBreaker.RecordSuccess();
                return result;
            }

            _logger.LogWarning("Model {ModelName} failed or was unavailable. Trying next candidate...", modelName);
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return null;
        }

        _circuitBreaker.RecordFailure();
        _logger.LogError("All candidate Gemini models failed to yield a response.");
        return null;
    }

    /// <summary>
    /// Executes the HTTP REST API call to Gemini with exponential backoff for transient 503 / 429 status codes.
    /// </summary>
    private async Task<TResponse?> TryExecuteRequestWithRetryAsync<TResponse>(string modelName, string prompt, JsonObject jsonSchema, CancellationToken cancellationToken) where TResponse : class
    {
        int maxRetries = 3;
        int delayMs = (int)RetryDelay.TotalMilliseconds;

        string requestUrl = $"https://generativelanguage.googleapis.com/v1beta/models/{modelName}:generateContent?key={_options.GeminiApiKey}";

        var requestPayload = BuildGeminiRequestPayload(prompt, jsonSchema);

        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                string jsonPayload = JsonSerializer.Serialize(requestPayload);
                using var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

                using var response = await _httpClient.PostAsync(requestUrl, content, cancellationToken).ConfigureAwait(false);

                if (response.IsSuccessStatusCode)
                {
                    string responseJson = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                    var parsed = ParseGeminiResponse<TResponse>(responseJson);
                    if (parsed is not null)
                    {
                        return parsed;
                    }
                    break;
                }

                int statusCode = (int)response.StatusCode;
                string errorBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

                // Retry on transient server failures (5xx) or rate limiting (429)
                if ((statusCode >= 500 || statusCode == 429) && attempt < maxRetries)
                {
                    _logger.LogWarning(
                        "Gemini API returned status {StatusCode} for model {Model}. Retrying attempt {Attempt}/{Max} in {Delay}ms...",
                        statusCode, modelName, attempt, maxRetries, delayMs);

                    await Task.Delay(delayMs, cancellationToken).ConfigureAwait(false);
                    delayMs *= 2; // Exponential backoff

                    // Add a longer wait on 503 Service Unavailable to give the server breathing room.
                    if (statusCode == 503)
                    {
                        _logger.LogWarning(
                            "Gemini API reports high demand (503). Adding extra {ExtraDelay}s delay before retry.",
                            ServiceUnavailableExtraDelay.TotalSeconds);
                        await Task.Delay(ServiceUnavailableExtraDelay, cancellationToken).ConfigureAwait(false);
                    }

                    continue;
                }

                _logger.LogError(
                    "Gemini API call to model {Model} failed with status code {StatusCode}: {ErrorBody}",
                    modelName, response.StatusCode, errorBody);
                break;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (IsTransient(ex) && attempt < maxRetries)
            {
                _logger.LogWarning(
                    ex,
                    "Gemini request for model {Model} encountered transient failure on attempt {Attempt}/{Max}: {Message}. Retrying in {Delay}ms...",
                    modelName, attempt, maxRetries, ex.Message, delayMs);

                await Task.Delay(delayMs, cancellationToken).ConfigureAwait(false);
                delayMs *= 2; // Exponential backoff
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Gemini request for model {Model} failed after {MaxRetries} attempts: {Message}",
                    modelName, maxRetries, ex.Message);
                break;
            }
        }

        return default;
    }

    /// <summary>
    /// Builds the structured JSON request payload sent to the Gemini generateContent endpoint.
    /// </summary>
    /// <param name="prompt">The prompt text submitted to the model.</param>
    /// <param name="jsonSchema">The JSON schema constraining the structured output.</param>
    /// <returns>The request payload object ready for serialization.</returns>
    private object BuildGeminiRequestPayload(string prompt, JsonObject jsonSchema)
    {
        return new
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
                responseJsonSchema = jsonSchema,
                maxOutputTokens = 8192 // Ensures large structured recipes finish generating completely without truncation
            }
        };
    }

    /// <summary>
    /// Parses a Gemini generateContent response JSON into the requested structured type.
    /// </summary>
    /// <typeparam name="TResponse">The target structured response type.</typeparam>
    /// <param name="responseJson">The raw JSON returned by the Gemini endpoint.</param>
    /// <returns>The deserialized response, or <see langword="null"/> when no usable candidate exists.</returns>
    private static TResponse? ParseGeminiResponse<TResponse>(string responseJson) where TResponse : class
    {
        using var doc = JsonDocument.Parse(responseJson);
        var root = doc.RootElement;
        if (root.TryGetProperty("candidates", out var candidates) && candidates.GetArrayLength() > 0)
        {
            string innerJson = candidates[0]
                .GetProperty("content")
                .GetProperty("parts")[0]
                .GetProperty("text")
                .GetString() ?? string.Empty;

            return JsonSerializer.Deserialize<TResponse>(innerJson, StructuredAnswerOptions);
        }

        return default;
    }
}