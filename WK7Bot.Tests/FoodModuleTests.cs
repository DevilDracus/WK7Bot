using System.Reflection;
using System.Runtime.CompilerServices;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using WK7Bot.Core.Exceptions;
using WK7Bot.Models;
using WK7Bot.Modules;
using WK7Bot.Services.Interfaces;
using Xunit;

namespace WK7Bot.Tests;

public class FoodModuleTests
{
    private const string FoodChannelMention = "<#424242424242424242>";

    private sealed class TestFoodModule : FoodModule
    {
        private readonly ITextChannel? _channel;

        public int DeferCallCount { get; private set; }
        public List<string> Followups { get; } = new();
        public Embed? PostedEmbed { get; private set; }

        public TestFoodModule(
            IGeminiFoodService geminiFoodService,
            IRecipeSearchService recipeSearchService,
            ITextChannel? channel,
            IRandomSource? randomSource = null)
            : base(geminiFoodService, recipeSearchService, NullLogger<FoodModule>.Instance, randomSource)
        {
            _channel = channel;
        }

        protected override ITextChannel? FindFoodChannel() => _channel;

        protected override Task PostToFoodChannelAsync(ITextChannel channel, Embed embed)
        {
            PostedEmbed = embed;
            return Task.CompletedTask;
        }

        protected override Task DeferAsync(bool ephemeral, RequestOptions options)
        {
            DeferCallCount++;
            return Task.CompletedTask;
        }

        protected override Task<IUserMessage> FollowupAsync(
            string text,
            Embed[] embeds,
            bool isTTS,
            bool ephemeral,
            AllowedMentions allowedMentions,
            RequestOptions options,
            MessageComponent components,
            Embed embed,
            PollProperties poll)
        {
            Followups.Add(text ?? string.Empty);
            return Task.FromResult<IUserMessage>(null!);
        }
    }

    private static TestFoodModule CreateModule(
        IGeminiFoodService service,
        ITextChannel? channel,
        IRecipeSearchService? searchService = null,
        string username = "Tester",
        IRandomSource? randomSource = null)
    {
        var module = new TestFoodModule(
            service,
            searchService ?? new Mock<IRecipeSearchService>().Object,
            channel,
            randomSource ?? new StubRandomSource());

        var context = (SocketInteractionContext)RuntimeHelpers.GetUninitializedObject(typeof(SocketInteractionContext));
        DiscordTestDoubles.SetUser(context, username);
        ((IInteractionModuleBase)module).SetContext(context);

        return module;
    }

    private static ITextChannel CreateFoodChannel()
    {
        var channel = new Mock<ITextChannel>();
        channel.SetupGet(c => c.Mention).Returns(FoodChannelMention);
        return channel.Object;
    }

    private static RenalRecipeData CreateRecipe() => new()
    {
        Title = "Gemüseauflauf",
        Description = "Saisonales Auflaufgericht mit renalen Zutaten.",
        PrepTime = "20 Minuten",
        CookTime = "40 Minuten",
        Servings = 4,
        SeasonalIngredientsUsed = new List<string> { "Zucchini" },
        Ingredients = new List<string> { "Zucchini", "Karotten" },
        Instructions = new List<string> { "Gemüse schneiden", "Im Ofen backen" },
        TransplantSafetyNotes = "Vollständig durchgaren.",
        DietTags = new List<string> { "low_potassium", "low_carb" }
    };

    private static SeasonalFoodData CreateProduce() => new()
    {
        Month = "Juli",
        Fruits = new List<string> { "Aprikose" },
        Vegetables = new List<string> { "Zucchini" },
        Herbs = new List<string> { "Basilikum" },
        Nuts = new List<string> { "Walnuss" }
    };

    [Fact]
    public void Constructor_Throws_WhenDependenciesAreNull()
    {
        Assert.Throws<ArgumentNullException>(() => new FoodModule(
            null!, new Mock<IRecipeSearchService>().Object, NullLogger<FoodModule>.Instance));
        Assert.Throws<ArgumentNullException>(() => new FoodModule(
            new Mock<IGeminiFoodService>().Object, null!, NullLogger<FoodModule>.Instance));
        Assert.Throws<ArgumentNullException>(() => new FoodModule(
            new Mock<IGeminiFoodService>().Object, new Mock<IRecipeSearchService>().Object, null!));
    }

    [Fact]
    public void RecipeSearchCommand_IsExposedAsSlashCommand()
    {
        var attribute = typeof(FoodModule)
            .GetMethod(nameof(FoodModule.SearchRecipeAsync))!
            .GetCustomAttribute<SlashCommandAttribute>();

        Assert.NotNull(attribute);
        Assert.Equal("recipe", attribute!.Name);
        Assert.Contains("🍎food", attribute.Description);

        var queryParameter = typeof(FoodModule)
            .GetMethod(nameof(FoodModule.SearchRecipeAsync))!
            .GetParameters()[0];
        Assert.Equal("query", queryParameter.GetCustomAttribute<SummaryAttribute>()?.Name);
        Assert.False(queryParameter.HasDefaultValue);
    }

    [Fact]
    public void RecipeGenerateCommand_IsExposedAsSlashCommand()
    {
        var attribute = typeof(FoodModule)
            .GetMethod(nameof(FoodModule.GenerateRecipeAsync))!
            .GetCustomAttribute<SlashCommandAttribute>();

        Assert.NotNull(attribute);
        Assert.Equal("recipe-generate", attribute!.Name);
        Assert.Contains("🍎food", attribute.Description);
    }

    [Fact]
    public void SeasonalProduceCommand_IsExposedAsSlashCommand()
    {
        var attribute = typeof(FoodModule)
            .GetMethod(nameof(FoodModule.GetSeasonalProduceAsync))!
            .GetCustomAttribute<SlashCommandAttribute>();

        Assert.NotNull(attribute);
        Assert.Equal("seasonal-produce", attribute!.Name);
        Assert.Contains("month", attribute.Description);
    }

    [Fact]
    public async Task GenerateRecipeAsync_ReportsMissingChannel_WhenFoodChannelIsAbsent()
    {
        var service = new Mock<IGeminiFoodService>();
        var module = CreateModule(service.Object, channel: null);

        await module.GenerateRecipeAsync();

        Assert.Equal(1, module.DeferCallCount);
        var followup = Assert.Single(module.Followups);
        Assert.Contains("wurde auf diesem Server nicht gefunden", followup);
        Assert.Contains("🍎food", followup);
        Assert.Null(module.PostedEmbed);
        service.Verify(s => s.GetWeeklyRenalRecipeAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GenerateRecipeAsync_ReportsFailure_WhenServiceReturnsNull()
    {
        var service = new Mock<IGeminiFoodService>();
        service.Setup(s => s.GetWeeklyRenalRecipeAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((RenalRecipeData?)null);
        var module = CreateModule(service.Object, CreateFoodChannel());

        await module.GenerateRecipeAsync();

        var followup = Assert.Single(module.Followups);
        Assert.Contains("konnte von der Gemini-API nicht erstellt werden", followup);
        Assert.Null(module.PostedEmbed);
    }

    [Fact]
    public async Task GenerateRecipeAsync_PostsRecipeEmbed_WhenServiceSucceeds()
    {
        var service = new Mock<IGeminiFoodService>();
        service.Setup(s => s.GetWeeklyRenalRecipeAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateRecipe());
        var module = CreateModule(service.Object, CreateFoodChannel());

        await module.GenerateRecipeAsync();

        Assert.Equal(1, module.DeferCallCount);
        var embed = module.PostedEmbed;
        Assert.NotNull(embed);
        Assert.Equal("🥗 Recipe of the Week: Gemüseauflauf", embed!.Title);
        Assert.Contains(embed.Fields, f => f.Name == "🛒 Ingredients" && f.Value.Contains("• Zucchini"));
        Assert.Contains(embed.Fields, f => f.Name == "👨‍🍳 Preparation Steps" && f.Value.Contains("1. Gemüse schneiden"));
        Assert.Contains(embed.Fields, f => f.Name == "🏷️ Diet Tags" && f.Value.Contains("Kaliumarm") && f.Value.Contains("Kohlenhydratarm"));
        Assert.Contains(embed.Fields, f => f.Name == "🛡️ Safety & Renal Notes" && f.Value.Contains("Vollständig durchgaren."));
        Assert.DoesNotContain(embed.Fields, f => f.Name == "⚠️ Hinweis");
        Assert.NotNull(embed.Footer);
        Assert.Contains("@Tester", embed.Footer!.Value.Text);

        var followup = Assert.Single(module.Followups);
        Assert.Contains("Rezept erfolgreich erstellt", followup);
        Assert.Contains(FoodChannelMention, followup);
    }

    [Fact]
    public async Task GenerateRecipeAsync_ReportsUnexpectedError_WhenServiceThrows()
    {
        var service = new Mock<IGeminiFoodService>();
        service.Setup(s => s.GetWeeklyRenalRecipeAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("gemini down"));
        var module = CreateModule(service.Object, CreateFoodChannel());

        await module.GenerateRecipeAsync();

        var followup = Assert.Single(module.Followups);
        Assert.Contains("unerwarteter Fehler", followup);
        Assert.Null(module.PostedEmbed);
    }

    [Fact]
    public async Task SearchRecipeAsync_PostsEmbedWithSourceLink_WhenSearchSucceeds()
    {
        var recipe = CreateRecipe();
        recipe.SourceUrl = "https://www.chefkoch.de/rezepte/pfannkuchen.html";
        recipe.DietTags = new List<string>();
        recipe.TransplantSafetyNotes = string.Empty;

        var gemini = new Mock<IGeminiFoodService>();
        var search = new Mock<IRecipeSearchService>();
        search.Setup(s => s.SearchWebRecipeAsync("Pfannkuchen", It.IsAny<CancellationToken>()))
            .ReturnsAsync(recipe);

        var module = CreateModule(gemini.Object, CreateFoodChannel(), search.Object);

        await module.SearchRecipeAsync("Pfannkuchen");

        Assert.Equal(1, module.DeferCallCount);
        gemini.Verify(s => s.FormatRecipeAsync(It.IsAny<RenalRecipeData>(), It.IsAny<CancellationToken>()), Times.Once);
        var embed = module.PostedEmbed;
        Assert.NotNull(embed);
        Assert.Equal("🍽️ Gemüseauflauf", embed!.Title);
        Assert.Equal(recipe.SourceUrl, embed.Url?.ToString());
        Assert.Contains(embed.Fields, f => f.Name == "🛒 Ingredients" && f.Value.Contains("• Zucchini"));
        Assert.Contains(embed.Fields, f => f.Name == "⚠️ Hinweis" && f.Value.Contains(recipe.SourceUrl));
        Assert.DoesNotContain(embed.Fields, f => f.Name == "🏷️ Diet Tags");
        Assert.DoesNotContain(embed.Fields, f => f.Name == "🛡️ Safety & Renal Notes");
        Assert.NotNull(embed.Footer);
        Assert.Contains("@Tester", embed.Footer!.Value.Text);

        var followup = Assert.Single(module.Followups);
        Assert.Contains("Rezept gefunden", followup);
        Assert.Contains(FoodChannelMention, followup);
    }

    [Fact]
    public async Task SearchRecipeAsync_ReportsFailure_WhenNoRecipeFound()
    {
        var search = new Mock<IRecipeSearchService>();
        search.Setup(s => s.SearchWebRecipeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((RenalRecipeData?)null);

        var module = CreateModule(new Mock<IGeminiFoodService>().Object, CreateFoodChannel(), search.Object);

        await module.SearchRecipeAsync("gibbnichts");

        var followup = Assert.Single(module.Followups);
        Assert.Contains("Kein auswertbares Rezept", followup);
        Assert.Contains("/recipe-generate", followup);
        Assert.Null(module.PostedEmbed);
    }

    [Fact]
    public async Task SearchRecipeAsync_ReportsMissingChannel_WhenFoodChannelIsAbsent()
    {
        var search = new Mock<IRecipeSearchService>();
        var module = CreateModule(new Mock<IGeminiFoodService>().Object, channel: null, search.Object);

        await module.SearchRecipeAsync("Pfannkuchen");

        var followup = Assert.Single(module.Followups);
        Assert.Contains("wurde auf diesem Server nicht gefunden", followup);
        Assert.Null(module.PostedEmbed);
        search.Verify(s => s.SearchWebRecipeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SearchRecipeAsync_RejectsBlankQuery()
    {
        var search = new Mock<IRecipeSearchService>();
        var module = CreateModule(new Mock<IGeminiFoodService>().Object, CreateFoodChannel(), search.Object);

        await module.SearchRecipeAsync("   ");

        var followup = Assert.Single(module.Followups);
        Assert.Contains("Bitte gib einen Suchbegriff an", followup);
        Assert.Null(module.PostedEmbed);
        search.Verify(s => s.SearchWebRecipeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SearchRecipeAsync_ReportsUnexpectedError_WhenSearchThrows()
    {
        var search = new Mock<IRecipeSearchService>();
        search.Setup(s => s.SearchWebRecipeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("network down"));

        var module = CreateModule(new Mock<IGeminiFoodService>().Object, CreateFoodChannel(), search.Object);

        await module.SearchRecipeAsync("Pfannkuchen");

        var followup = Assert.Single(module.Followups);
        Assert.Contains("unerwarteter Fehler", followup);
        Assert.Null(module.PostedEmbed);
    }

    [Fact]
    public async Task SearchRecipeAsync_ReportsUnavailable_WhenSearchProvidersAreBlocked()
    {
        var search = new Mock<IRecipeSearchService>();
        search.Setup(s => s.SearchWebRecipeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RecipeSearchUnavailableException("No recipe search provider responded."));

        var module = CreateModule(new Mock<IGeminiFoodService>().Object, CreateFoodChannel(), search.Object);

        await module.SearchRecipeAsync("Pfannkuchen");

        var followup = Assert.Single(module.Followups);
        Assert.Contains("vorübergehend nicht verfügbar", followup);
        Assert.Contains("/recipe-generate", followup);
        Assert.Null(module.PostedEmbed);
    }

    [Fact]
    public async Task SearchRecipeAsync_PostsGeminiFormattedRecipe_WhenFormattingSucceeds()
    {
        var raw = CreateRecipe();
        raw.SourceUrl = "https://www.chefkoch.de/rezepte/123/pfannkuchen.html";

        var formatted = CreateRecipe();
        formatted.Title = "Sauber formatierter Titel";
        formatted.Ingredients = new List<string> { "200 g Mehl", "300 ml Milch" };
        formatted.SourceUrl = raw.SourceUrl;
        formatted.DietTags = new List<string>();
        formatted.TransplantSafetyNotes = string.Empty;

        var gemini = new Mock<IGeminiFoodService>();
        gemini.Setup(s => s.FormatRecipeAsync(It.IsAny<RenalRecipeData>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(formatted);

        var search = new Mock<IRecipeSearchService>();
        search.Setup(s => s.SearchWebRecipeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(raw);

        var module = CreateModule(gemini.Object, CreateFoodChannel(), search.Object);

        await module.SearchRecipeAsync("Pfannkuchen");

        gemini.Verify(s => s.FormatRecipeAsync(It.IsAny<RenalRecipeData>(), It.IsAny<CancellationToken>()), Times.Once);

        var embed = module.PostedEmbed;
        Assert.NotNull(embed);
        Assert.Equal("🍽️ Sauber formatierter Titel", embed!.Title);
        Assert.Equal(raw.SourceUrl, embed.Url?.ToString());
        Assert.Contains(embed.Fields, f => f.Name == "🛒 Ingredients" && f.Value.Contains("300 ml Milch"));
        Assert.Contains(embed.Fields, f => f.Name == "⚠️ Hinweis" && f.Value.Contains(raw.SourceUrl));
    }

    [Fact]
    public async Task SearchRecipeAsync_PostsRawRecipe_WhenFormattingFails()
    {
        var raw = CreateRecipe();
        raw.SourceUrl = "https://www.chefkoch.de/rezepte/123/pfannkuchen.html";

        var gemini = new Mock<IGeminiFoodService>();
        gemini.Setup(s => s.FormatRecipeAsync(It.IsAny<RenalRecipeData>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("gemini down"));

        var search = new Mock<IRecipeSearchService>();
        search.Setup(s => s.SearchWebRecipeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(raw);

        var module = CreateModule(gemini.Object, CreateFoodChannel(), search.Object);

        await module.SearchRecipeAsync("Pfannkuchen");

        var embed = module.PostedEmbed;
        Assert.NotNull(embed);
        Assert.Equal($"🍽️ {raw.Title}", embed!.Title);
        Assert.Equal(raw.SourceUrl, embed.Url?.ToString());

        var followup = Assert.Single(module.Followups);
        Assert.Contains("Rezept gefunden", followup);
    }

    [Fact]
    public void RecipeSeasonalCommand_IsExposedAsSlashCommand()
    {
        var method = typeof(FoodModule).GetMethod(nameof(FoodModule.SearchSeasonalRecipeAsync))!;
        var attribute = method.GetCustomAttribute<SlashCommandAttribute>();

        Assert.NotNull(attribute);
        Assert.Equal("recipe-seasonal", attribute!.Name);
        Assert.Contains("🍎food", attribute.Description);

        var queryParameter = method.GetParameters()[0];
        Assert.Equal("query", queryParameter.GetCustomAttribute<SummaryAttribute>()?.Name);
        Assert.True(queryParameter.HasDefaultValue);
    }

    [Fact]
    public async Task SearchSeasonalRecipeAsync_PostsRecipeSeededWithSeasonalProduce()
    {
        var gemini = new Mock<IGeminiFoodService>();
        gemini.Setup(s => s.GetSeasonalProduceAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateProduce());
        gemini.Setup(s => s.FormatRecipeAsync(It.IsAny<RenalRecipeData>(), It.IsAny<CancellationToken>()))
            .Returns((RenalRecipeData input, CancellationToken _) => Task.FromResult<RenalRecipeData?>(input));

        var recipe = CreateRecipe();
        recipe.SourceUrl = "https://www.chefkoch.de/rezepte/42/saison.html";
        recipe.DietTags = new List<string>();
        recipe.TransplantSafetyNotes = string.Empty;

        var search = new Mock<IRecipeSearchService>();
        search.Setup(s => s.SearchWebRecipeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(recipe);

        var module = CreateModule(gemini.Object, CreateFoodChannel(), search.Object);

        await module.SearchSeasonalRecipeAsync();

        search.Verify(
            s => s.SearchWebRecipeAsync(
                It.Is<string>(q => q.Contains("Zucchini") && q.Contains("Aprikose")),
                It.IsAny<CancellationToken>()),
            Times.Once);
        gemini.Verify(s => s.FormatRecipeAsync(It.IsAny<RenalRecipeData>(), It.IsAny<CancellationToken>()), Times.Once);

        var embed = module.PostedEmbed;
        Assert.NotNull(embed);
        Assert.Equal($"🍽️ {recipe.Title}", embed!.Title);
        Assert.Contains(embed.Fields, f => f.Name == "🌿 Saisonale Zutaten" && f.Value.Contains("Zucchini") && f.Value.Contains("Aprikose"));
        Assert.Contains(embed.Fields, f => f.Name == "⚠️ Hinweis" && f.Value.Contains(recipe.SourceUrl));
        Assert.NotNull(embed.Footer);
        Assert.Contains("Saisonale Zutaten:", embed.Footer!.Value.Text);

        var followup = Assert.Single(module.Followups);
        Assert.Contains("Saisonales Rezept", followup);
        Assert.Contains(FoodChannelMention, followup);
    }

    [Fact]
    public async Task SearchSeasonalRecipeAsync_AppendsSeasonalTerm_WhenQueryIsGiven()
    {
        var gemini = new Mock<IGeminiFoodService>();
        gemini.Setup(s => s.GetSeasonalProduceAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateProduce());

        var recipe = CreateRecipe();
        recipe.SourceUrl = "https://www.chefkoch.de/rezepte/42/saison.html";

        var search = new Mock<IRecipeSearchService>();
        search.Setup(s => s.SearchWebRecipeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(recipe);

        var module = CreateModule(gemini.Object, CreateFoodChannel(), search.Object);

        await module.SearchSeasonalRecipeAsync("Pfannkuchen");

        search.Verify(
            s => s.SearchWebRecipeAsync(
                It.Is<string>(q => q.StartsWith("Pfannkuchen") && (q.Contains("Zucchini") || q.Contains("Aprikose"))),
                It.IsAny<CancellationToken>()),
            Times.Once);
        Assert.NotNull(module.PostedEmbed);
    }

    [Fact]
    public async Task SearchSeasonalRecipeAsync_UsesInjectedRandomness_ForSeasonalTerms()
    {
        var gemini = new Mock<IGeminiFoodService>();
        gemini.Setup(s => s.GetSeasonalProduceAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateProduce());

        var recipe = CreateRecipe();
        recipe.SourceUrl = "https://www.chefkoch.de/rezepte/42/saison.html";

        var search = new Mock<IRecipeSearchService>();
        search.Setup(s => s.SearchWebRecipeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(recipe);

        var module = CreateModule(
            gemini.Object,
            CreateFoodChannel(),
            search.Object,
            randomSource: new StubRandomSource(9));

        await module.SearchSeasonalRecipeAsync();

        // Roll 9 of 10 total weight points picks the nut, the second roll 0 the heaviest vegetable.
        search.Verify(
            s => s.SearchWebRecipeAsync(
                It.Is<string>(q => q.Contains("Walnuss") && q.Contains("Zucchini")),
                It.IsAny<CancellationToken>()),
            Times.Once);
        var embed = module.PostedEmbed;
        Assert.NotNull(embed);
        Assert.Contains(embed!.Fields, f => f.Name == "🌿 Saisonale Zutaten" && f.Value.Contains("Walnuss"));
    }

    [Fact]
    public async Task SearchSeasonalRecipeAsync_ReportsFailure_WhenProduceUnavailable()
    {
        var gemini = new Mock<IGeminiFoodService>();
        gemini.Setup(s => s.GetSeasonalProduceAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((SeasonalFoodData?)null);

        var search = new Mock<IRecipeSearchService>();
        var module = CreateModule(gemini.Object, CreateFoodChannel(), search.Object);

        await module.SearchSeasonalRecipeAsync();

        var followup = Assert.Single(module.Followups);
        Assert.Contains("Saisonprodukte konnten nicht", followup);
        Assert.Null(module.PostedEmbed);
        search.Verify(s => s.SearchWebRecipeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SearchSeasonalRecipeAsync_ReportsFailure_WhenNoRecipeFound()
    {
        var gemini = new Mock<IGeminiFoodService>();
        gemini.Setup(s => s.GetSeasonalProduceAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateProduce());

        var search = new Mock<IRecipeSearchService>();
        search.Setup(s => s.SearchWebRecipeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((RenalRecipeData?)null);

        var module = CreateModule(gemini.Object, CreateFoodChannel(), search.Object);

        await module.SearchSeasonalRecipeAsync();

        var followup = Assert.Single(module.Followups);
        Assert.Contains("Kein auswertbares Rezept", followup);
        Assert.Null(module.PostedEmbed);
        gemini.Verify(s => s.FormatRecipeAsync(It.IsAny<RenalRecipeData>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SearchSeasonalRecipeAsync_ReportsUnavailable_WhenSearchProvidersAreBlocked()
    {
        var gemini = new Mock<IGeminiFoodService>();
        gemini.Setup(s => s.GetSeasonalProduceAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateProduce());

        var search = new Mock<IRecipeSearchService>();
        search.Setup(s => s.SearchWebRecipeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RecipeSearchUnavailableException("No recipe search provider responded."));

        var module = CreateModule(gemini.Object, CreateFoodChannel(), search.Object);

        await module.SearchSeasonalRecipeAsync();

        var followup = Assert.Single(module.Followups);
        Assert.Contains("vorübergehend nicht verfügbar", followup);
        Assert.Contains("/recipe-generate", followup);
        Assert.Null(module.PostedEmbed);
    }

    [Fact]
    public async Task GetSeasonalProduceAsync_ReportsMissingChannel_WhenFoodChannelIsAbsent()
    {
        var service = new Mock<IGeminiFoodService>();
        var module = CreateModule(service.Object, channel: null);

        await module.GetSeasonalProduceAsync(7);

        var followup = Assert.Single(module.Followups);
        Assert.Contains("wurde auf diesem Server nicht gefunden", followup);
        Assert.Null(module.PostedEmbed);
        service.Verify(s => s.GetSeasonalProduceAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetSeasonalProduceAsync_RejectsMonthOutsideValidRange()
    {
        var service = new Mock<IGeminiFoodService>();
        var module = CreateModule(service.Object, CreateFoodChannel());

        await module.GetSeasonalProduceAsync(13);

        var followup = Assert.Single(module.Followups);
        Assert.Contains("Ungültiger Monat", followup);
        Assert.Null(module.PostedEmbed);
        service.Verify(s => s.GetSeasonalProduceAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetSeasonalProduceAsync_UsesCurrentMonth_WhenMonthOmitted()
    {
        // The production code reads DateTime.Now itself, so a run straddling midnight must not
        // fail the assertion: accept the captured month or the month that had just begun.
        DateTime expected = DateTime.Now;
        var service = new Mock<IGeminiFoodService>();
        service.Setup(s => s.GetSeasonalProduceAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((SeasonalFoodData?)null);
        var module = CreateModule(service.Object, CreateFoodChannel());

        await module.GetSeasonalProduceAsync();

        service.Verify(
            s => s.GetSeasonalProduceAsync(
                It.Is<DateTime>(d => d.Day == 1 && (IsSameMonth(d, expected) || IsNextMonth(d, expected))),
                It.IsAny<CancellationToken>()),
            Times.Once);

        var followup = Assert.Single(module.Followups);
        Assert.Contains("Saisonprodukte konnten nicht", followup);
    }

    [Fact]
    public async Task GetSeasonalProduceAsync_PostsSeasonalEmbed_WhenServiceSucceeds()
    {
        var service = new Mock<IGeminiFoodService>();
        service.Setup(s => s.GetSeasonalProduceAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateProduce());
        var module = CreateModule(service.Object, CreateFoodChannel());

        await module.GetSeasonalProduceAsync(7);

        Assert.Equal(1, module.DeferCallCount);
        var embed = module.PostedEmbed;
        Assert.NotNull(embed);
        Assert.Equal("🌱 Saisonkalender: Juli", embed!.Title);
        Assert.Contains("**Juli**", embed.Description);
        Assert.Contains(embed.Fields, f => f.Name == "🍎 Obst" && f.Value == "Aprikose");
        Assert.Contains(embed.Fields, f => f.Name == "🥕 Gemüse" && f.Value == "Zucchini");
        Assert.Contains(embed.Fields, f => f.Name == "🌿 Kräuter" && f.Value == "Basilikum");
        Assert.Contains(embed.Fields, f => f.Name == "🌰 Nüsse" && f.Value == "Walnuss");
        Assert.NotNull(embed.Footer);
        Assert.Contains("@Tester", embed.Footer!.Value.Text);

        var followup = Assert.Single(module.Followups);
        Assert.Contains("erfolgreich in", followup);
        Assert.Contains(FoodChannelMention, followup);
    }

    [Fact]
    public async Task GetSeasonalProduceAsync_PostsPlaceholder_WhenCategoryIsEmpty()
    {
        var produce = CreateProduce();
        produce.Nuts = new List<string>();

        var service = new Mock<IGeminiFoodService>();
        service.Setup(s => s.GetSeasonalProduceAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(produce);
        var module = CreateModule(service.Object, CreateFoodChannel());

        await module.GetSeasonalProduceAsync(7);

        var embed = module.PostedEmbed;
        Assert.NotNull(embed);
        Assert.Contains(embed!.Fields, f => f.Name == "🌰 Nüsse" && f.Value == "Keine angegeben");
    }

    [Fact]
    public async Task GetSeasonalProduceAsync_ReportsFailure_WhenServiceReturnsNull()
    {
        var service = new Mock<IGeminiFoodService>();
        service.Setup(s => s.GetSeasonalProduceAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((SeasonalFoodData?)null);
        var module = CreateModule(service.Object, CreateFoodChannel());

        await module.GetSeasonalProduceAsync(7);

        var followup = Assert.Single(module.Followups);
        Assert.Contains("Saisonprodukte konnten nicht", followup);
        Assert.Null(module.PostedEmbed);
    }

    [Fact]
    public async Task GetSeasonalProduceAsync_ReportsUnexpectedError_WhenServiceThrows()
    {
        var service = new Mock<IGeminiFoodService>();
        service.Setup(s => s.GetSeasonalProduceAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("gemini down"));
        var module = CreateModule(service.Object, CreateFoodChannel());

        await module.GetSeasonalProduceAsync(7);

        var followup = Assert.Single(module.Followups);
        Assert.Contains("unerwarteter Fehler", followup);
        Assert.Null(module.PostedEmbed);
    }

    /// <summary>
    /// Tolerates a test run that straddles a month boundary: the command reads <see cref="DateTime.Now"/>
    /// itself, so the captured month and the month used by the command may legitimately differ by one.
    /// </summary>
    private static bool IsSameMonth(DateTime actual, DateTime expected)
        => actual.Year == expected.Year && actual.Month == expected.Month;

    /// <summary>
    /// Determines whether <paramref name="actual"/> is the first day of the month following the
    /// captured month.
    /// </summary>
    private static bool IsNextMonth(DateTime actual, DateTime expected)
    {
        var firstOfCaptured = new DateTime(expected.Year, expected.Month, 1);
        var firstOfNext = firstOfCaptured.AddMonths(1);
        return actual.Year == firstOfNext.Year && actual.Month == firstOfNext.Month;
    }
}
