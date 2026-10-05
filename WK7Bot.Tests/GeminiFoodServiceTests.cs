using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using WK7Bot.Models;
using WK7Bot.Options;
using WK7Bot.Services;
using Xunit;

namespace WK7Bot.Tests;

public class GeminiFoodServiceTests
{
    private const string TestApiKey = "gemini-test-key";

    private const string ProduceJson = """
    {
      "month": "Juli",
      "fruits": ["Aprikose", "Pfirsich"],
      "vegetables": ["Zucchini", "Bohnen"],
      "herbs": ["Basilikum", "Petersilie"],
      "nuts": ["Walnuss", "Haselnuss"]
    }
    """;

    private const string RecipeJson = """
    {
      "title": "Gemüseauflauf",
      "description": "Saisonales Auflaufgericht mit renalen Zutaten.",
      "prep_time": "20 Minuten",
      "cook_time": "40 Minuten",
      "servings": 4,
      "seasonal_ingredients_used": ["Zucchini"],
      "ingredients": ["Zucchini", "Karotten"],
      "instructions": ["Gemüse schneiden", "Im Ofen backen"],
      "nutrition_per_serving": {
        "calories_kcal": 320,
        "protein_g": 12.5,
        "carbohydrates_g": 34.0,
        "fat_g": 9.2,
        "sodium_mg": 180.0,
        "potassium_kalium_mg": 450.0,
        "sulfate_mg": 70.0,
        "phosphorus_mg": 210.0
      },
      "transplant_safety_notes": "Vollständig durchgaren."
    }
    """;

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            _responder = responder;
        }

        public int CallCount { get; private set; }
        public List<string> RequestUrls { get; } = new();
        public List<string> Payloads { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            RequestUrls.Add(request.RequestUri!.ToString());
            Payloads.Add(request.Content is null
                ? string.Empty
                : request.Content.ReadAsStringAsync().GetAwaiter().GetResult());
            return Task.FromResult(_responder(request));
        }
    }

    private static GeminiFoodService CreateService(HttpMessageHandler handler, string? apiKey = TestApiKey)
    {
        var options = Microsoft.Extensions.Options.Options.Create(new Wk7BotOptions { GeminiApiKey = apiKey });

        return new GeminiFoodService(
            new HttpClient(handler),
            options,
            NullLogger<GeminiFoodService>.Instance);
    }

    private static HttpResponseMessage Json(string json, HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        return new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    private static string Envelope(string innerJson)
    {
        var envelope = new JsonObject
        {
            ["candidates"] = new JsonArray
            {
                new JsonObject
                {
                    ["content"] = new JsonObject
                    {
                        ["parts"] = new JsonArray
                        {
                            new JsonObject { ["text"] = innerJson }
                        }
                    }
                }
            }
        };

        return envelope.ToJsonString();
    }

    private static string ModelName(string requestUrl)
    {
        const string prefix = "/models/";
        int start = requestUrl.IndexOf(prefix, StringComparison.Ordinal) + prefix.Length;
        int end = requestUrl.IndexOf(':', start);
        return requestUrl[start..end];
    }

    [Fact]
    public void Constructor_Throws_WhenDependenciesAreNull()
    {
        var options = Microsoft.Extensions.Options.Options.Create(new Wk7BotOptions { GeminiApiKey = TestApiKey });

        Assert.Throws<ArgumentNullException>(() => new GeminiFoodService(null!, options, NullLogger<GeminiFoodService>.Instance));
        Assert.Throws<ArgumentNullException>(() => new GeminiFoodService(new HttpClient(), null!, NullLogger<GeminiFoodService>.Instance));
        Assert.Throws<ArgumentNullException>(() => new GeminiFoodService(new HttpClient(), options, null!));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task GetSeasonalProduceAsync_ReturnsNull_WhenApiKeyMissing(string? apiKey)
    {
        var handler = new StubHttpMessageHandler(_ => Json("{}"));
        var service = CreateService(handler, apiKey);

        var result = await service.GetSeasonalProduceAsync(new DateTime(2026, 7, 1));

        Assert.Null(result);
        Assert.Equal(0, handler.CallCount);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task GetWeeklyRenalRecipeAsync_ReturnsNull_WhenApiKeyMissing(string? apiKey)
    {
        var handler = new StubHttpMessageHandler(_ => Json("{}"));
        var service = CreateService(handler, apiKey);

        var result = await service.GetWeeklyRenalRecipeAsync(new DateTime(2026, 7, 1));

        Assert.Null(result);
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task GetSeasonalProduceAsync_ParsesStructuredResponse()
    {
        var handler = new StubHttpMessageHandler(_ => Json(Envelope(ProduceJson)));
        var service = CreateService(handler);

        var result = await service.GetSeasonalProduceAsync(new DateTime(2026, 7, 1));

        Assert.NotNull(result);
        Assert.Equal("Juli", result!.Month);
        Assert.Equal(new[] { "Aprikose", "Pfirsich" }, result.Fruits);
        Assert.Equal(new[] { "Zucchini", "Bohnen" }, result.Vegetables);
        Assert.Equal(new[] { "Basilikum", "Petersilie" }, result.Herbs);
        Assert.Equal(new[] { "Walnuss", "Haselnuss" }, result.Nuts);
    }

    [Fact]
    public async Task GetWeeklyRenalRecipeAsync_ParsesStructuredResponseIncludingNutrition()
    {
        var handler = new StubHttpMessageHandler(_ => Json(Envelope(RecipeJson)));
        var service = CreateService(handler);

        var result = await service.GetWeeklyRenalRecipeAsync(new DateTime(2026, 7, 1));

        Assert.NotNull(result);
        Assert.Equal("Gemüseauflauf", result!.Title);
        Assert.Equal("Saisonales Auflaufgericht mit renalen Zutaten.", result.Description);
        Assert.Equal("20 Minuten", result.PrepTime);
        Assert.Equal("40 Minuten", result.CookTime);
        Assert.Equal(4, result.Servings);
        Assert.Equal(new[] { "Zucchini" }, result.SeasonalIngredientsUsed);
        Assert.Equal(new[] { "Zucchini", "Karotten" }, result.Ingredients);
        Assert.Equal(new[] { "Gemüse schneiden", "Im Ofen backen" }, result.Instructions);
        Assert.Equal("Vollständig durchgaren.", result.TransplantSafetyNotes);

        Assert.Equal(320, result.Nutrition.CaloriesKcal);
        Assert.Equal(12.5, result.Nutrition.ProteinGrams);
        Assert.Equal(34.0, result.Nutrition.CarbohydratesGrams);
        Assert.Equal(9.2, result.Nutrition.FatGrams);
        Assert.Equal(180.0, result.Nutrition.SodiumMg);
        Assert.Equal(450.0, result.Nutrition.PotassiumKaliumMg);
        Assert.Equal(70.0, result.Nutrition.SulfateMg);
        Assert.Equal(210.0, result.Nutrition.PhosphorusMg);
    }

    [Fact]
    public async Task GetSeasonalProduceAsync_SendsStructuredJsonRequestWithApiKey()
    {
        var handler = new StubHttpMessageHandler(_ => Json(Envelope(ProduceJson)));
        var service = CreateService(handler);

        await service.GetSeasonalProduceAsync(new DateTime(2026, 7, 1));

        var url = Assert.Single(handler.RequestUrls);
        Assert.Contains("generativelanguage.googleapis.com", url);
        Assert.Contains(":generateContent", url);
        Assert.Contains($"key={TestApiKey}", url);

        var payload = Assert.Single(handler.Payloads);
        Assert.Contains("Provide a complete list of seasonal produce", payload);
        Assert.Contains("\"responseMimeType\":\"application/json\"", payload);
        Assert.Contains("\"responseSchema\"", payload);
        Assert.Contains("\"maxOutputTokens\":8192", payload);
    }

    [Fact]
    public async Task GetWeeklyRenalRecipeAsync_SendsDietaryRestrictedPrompt()
    {
        var handler = new StubHttpMessageHandler(_ => Json(Envelope(RecipeJson)));
        var service = CreateService(handler);

        await service.GetWeeklyRenalRecipeAsync(new DateTime(2026, 7, 1));

        var payload = Assert.Single(handler.Payloads);
        Assert.Contains("Create a delicious recipe for", payload);
        Assert.Contains("dialysis or kidney transplant recipients", payload);
        Assert.Contains("grapefruit", payload);
        Assert.Contains("\"nutrition_per_serving\"", payload);
    }

    [Fact]
    public async Task GetSeasonalProduceAsync_FallsBackToSecondaryModel_WhenPrimaryFails()
    {
        int calls = 0;
        var handler = new StubHttpMessageHandler(_ =>
            ++calls == 1
                ? new HttpResponseMessage(HttpStatusCode.InternalServerError)
                : Json(Envelope(ProduceJson)));
        var service = CreateService(handler);

        var result = await service.GetSeasonalProduceAsync(new DateTime(2026, 7, 1));

        Assert.NotNull(result);
        Assert.Equal(2, handler.CallCount);
        Assert.NotEqual(ModelName(handler.RequestUrls[0]), ModelName(handler.RequestUrls[1]));
    }

    [Fact]
    public async Task GetSeasonalProduceAsync_ReturnsNull_WhenAllModelsFail()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var service = CreateService(handler);

        var result = await service.GetSeasonalProduceAsync(new DateTime(2026, 7, 1));

        Assert.Null(result);
        Assert.Equal(2, handler.CallCount);
    }

    [Fact]
    public async Task GetSeasonalProduceAsync_ReturnsNull_WhenResponseHasNoCandidates()
    {
        var handler = new StubHttpMessageHandler(_ => Json("""{"error":"no content"}"""));
        var service = CreateService(handler);

        var result = await service.GetSeasonalProduceAsync(new DateTime(2026, 7, 1));

        Assert.Null(result);
        Assert.Equal(2, handler.CallCount);
    }

    [Fact]
    public async Task GetSeasonalProduceAsync_RetriesTransientFailure_ThenSucceeds()
    {
        int calls = 0;
        var handler = new StubHttpMessageHandler(_ =>
            ++calls == 1
                ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                : Json(Envelope(ProduceJson)));
        var service = CreateService(handler);

        var result = await service.GetSeasonalProduceAsync(new DateTime(2026, 7, 1));

        Assert.NotNull(result);
        Assert.Equal(2, handler.CallCount);
    }

    [Fact]
    public async Task GetWeeklyRenalRecipeAsync_ReturnsNull_WhenTransportKeepsThrowing()
    {
        var handler = new StubHttpMessageHandler(_ => throw new HttpRequestException("connection reset"));
        var service = CreateService(handler);

        var result = await service.GetWeeklyRenalRecipeAsync(new DateTime(2026, 7, 1));

        Assert.Null(result);
        Assert.Equal(6, handler.CallCount);
    }

    [Fact]
    public async Task GetSeasonalProduceAsync_Throws_WhenCancellationTokenIsCancelled()
    {
        var handler = new StubHttpMessageHandler(_ => Json(Envelope(ProduceJson)));
        var service = CreateService(handler);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.GetSeasonalProduceAsync(new DateTime(2026, 7, 1), cancellation.Token));
    }
}
