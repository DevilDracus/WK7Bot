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
      "diet_tags": ["low_potassium", "low_phosphate", "low_carb"],
      "transplant_safety_notes": "Vollständig durchgaren."
    }
    """;

    private const string FormattedRecipeJson = """
    {
      "title": "Pfannkuchen (bereinigt)",
      "description": "Klassischer Pfannkuchenteig aus der Pfanne.",
      "prep_time": "10 Minuten",
      "cook_time": "15 Minuten",
      "servings": 4,
      "ingredients": ["200 g Mehl", "300 ml Milch", "2 Eier"],
      "instructions": ["Teig rühren", "In der Pfanne goldbraun backen"],
      "diet_tags": ["low_carb"],
      "transplant_safety_notes": "Nicht bewertet.",
      "seasonal_ingredients_used": ["Zucchini"]
    }
    """;

    private const string UnusableFormattedRecipeJson = """
    {
      "title": "Ohne Inhalt",
      "description": "",
      "prep_time": "",
      "cook_time": "",
      "servings": 0,
      "ingredients": [],
      "instructions": []
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
    public async Task GetWeeklyRenalRecipeAsync_ParsesStructuredResponseIncludingDietTags()
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
        Assert.Equal(new[] { "low_potassium", "low_phosphate", "low_carb" }, result.DietTags);
        Assert.Equal("Vollständig durchgaren.", result.TransplantSafetyNotes);
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
        Assert.Contains("\"diet_tags\"", payload);
        Assert.Contains("low_potassium", payload);
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

    private static RenalRecipeData ScrapedRecipe() => new()
    {
        Title = "Pfannkuchen roh vom Scraper",
        Ingredients = new List<string> { "200 g Mehl und 300 ml Milch und 2 Eier" },
        Instructions = new List<string> { "Alles in eine Schüssel geben und verrühren und dann in der Pfanne backen." },
        SourceUrl = "https://www.chefkoch.de/rezepte/123/pfannkuchen.html",
        ImageUrl = "https://images.example.com/pfannkuchen.jpg"
    };

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task FormatRecipeAsync_ReturnsNull_WhenApiKeyMissing(string? apiKey)
    {
        var handler = new StubHttpMessageHandler(_ => Json("{}"));
        var service = CreateService(handler, apiKey);

        var result = await service.FormatRecipeAsync(ScrapedRecipe());

        Assert.Null(result);
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task FormatRecipeAsync_ParsesFormattedRecipe_AndKeepsProvenanceWithoutMedicalClaims()
    {
        var handler = new StubHttpMessageHandler(_ => Json(Envelope(FormattedRecipeJson)));
        var service = CreateService(handler);
        var scraped = ScrapedRecipe();

        var result = await service.FormatRecipeAsync(scraped);

        Assert.NotNull(result);
        Assert.Equal("Pfannkuchen (bereinigt)", result!.Title);
        Assert.Equal(new[] { "200 g Mehl", "300 ml Milch", "2 Eier" }, result.Ingredients);
        Assert.Equal(new[] { "Teig rühren", "In der Pfanne goldbraun backen" }, result.Instructions);
        Assert.Equal(4, result.Servings);

        Assert.Equal(scraped.SourceUrl, result.SourceUrl);
        Assert.Equal(scraped.ImageUrl, result.ImageUrl);
        Assert.Empty(result.DietTags);
        Assert.Equal(string.Empty, result.TransplantSafetyNotes);
        Assert.Empty(result.SeasonalIngredientsUsed);
    }

    [Fact]
    public async Task FormatRecipeAsync_ReturnsNull_WhenStructuredOutputIsUnusable()
    {
        var handler = new StubHttpMessageHandler(_ => Json(Envelope(UnusableFormattedRecipeJson)));
        var service = CreateService(handler);

        var result = await service.FormatRecipeAsync(ScrapedRecipe());

        Assert.Null(result);
    }

    [Fact]
    public async Task FormatRecipeAsync_SendsScrapedRecipeAndSchemaWithoutDietTags()
    {
        var handler = new StubHttpMessageHandler(_ => Json(Envelope(FormattedRecipeJson)));
        var service = CreateService(handler);

        await service.FormatRecipeAsync(ScrapedRecipe());

        var payload = Assert.Single(handler.Payloads);
        Assert.Contains("Re-format it into the schema", payload);
        Assert.Contains("FAITHFUL", payload);
        Assert.Contains("200 g Mehl und 300 ml Milch und 2 Eier", payload);
        Assert.Contains("\"responseSchema\"", payload);
        Assert.Contains("\"instructions\"", payload);
        Assert.DoesNotContain("\"diet_tags\"", payload);
        Assert.DoesNotContain("transplant_safety_notes", payload);
    }

    [Fact]
    public async Task FormatRecipeAsync_Throws_WhenRecipeIsNull()
    {
        var handler = new StubHttpMessageHandler(_ => Json("{}"));
        var service = CreateService(handler);

        await Assert.ThrowsAsync<ArgumentNullException>(() => service.FormatRecipeAsync(null!));
    }
}
