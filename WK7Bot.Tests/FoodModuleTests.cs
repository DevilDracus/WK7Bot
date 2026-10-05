using System.Reflection;
using System.Runtime.CompilerServices;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
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

        public TestFoodModule(IGeminiFoodService geminiFoodService, ITextChannel? channel)
            : base(geminiFoodService, NullLogger<FoodModule>.Instance)
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

    private static TestFoodModule CreateModule(IGeminiFoodService service, ITextChannel? channel, string username = "Tester")
    {
        var module = new TestFoodModule(service, channel);

        var context = (SocketInteractionContext)RuntimeHelpers.GetUninitializedObject(typeof(SocketInteractionContext));
        SetUser(context, username);
        ((IInteractionModuleBase)module).SetContext(context);

        return module;
    }

    private static void SetUser(SocketInteractionContext context, string username)
    {
        var userField = FindField(context.GetType(), "<User>k__BackingField")
            ?? throw new InvalidOperationException("SocketInteractionContext user backing field not found.");
        userField.SetValue(context, CreateUser(username));
    }

    private static FieldInfo? FindField(Type type, string fieldName)
    {
        while (type != null)
        {
            var field = type.GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (field != null)
            {
                return field;
            }

            type = type.BaseType!;
        }

        return null;
    }

    private static SocketUser CreateUser(string username)
    {
        var userType = typeof(SocketUser).Assembly.GetType("Discord.WebSocket.SocketGlobalUser")
            ?? throw new InvalidOperationException("Discord.WebSocket.SocketGlobalUser type not found.");

        var user = (SocketUser)RuntimeHelpers.GetUninitializedObject(userType);
        var usernameProperty = userType.GetProperty("Username", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("SocketUser.Username property not found.");
        usernameProperty.SetValue(user, username);

        if (!string.Equals(user.Username, username, StringComparison.Ordinal))
        {
            var backingField = FindField(userType, "<Username>k__BackingField")
                ?? throw new InvalidOperationException("SocketUser.Username backing field not found.");
            backingField.SetValue(user, username);
        }

        return user;
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
        Nutrition = new RecipeNutrition
        {
            CaloriesKcal = 320,
            ProteinGrams = 12.5,
            CarbohydratesGrams = 34.0,
            FatGrams = 9.2,
            SodiumMg = 180.0,
            PotassiumKaliumMg = 450.0,
            SulfateMg = 70.0,
            PhosphorusMg = 210.0
        }
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
        Assert.Throws<ArgumentNullException>(() => new FoodModule(null!, NullLogger<FoodModule>.Instance));
        Assert.Throws<ArgumentNullException>(() => new FoodModule(new Mock<IGeminiFoodService>().Object, null!));
    }

    [Fact]
    public void RecipeCommand_IsExposedAsSlashCommand()
    {
        var attribute = typeof(FoodModule)
            .GetMethod(nameof(FoodModule.GenerateRecipeAsync))!
            .GetCustomAttribute<SlashCommandAttribute>();

        Assert.NotNull(attribute);
        Assert.Equal("recipe", attribute!.Name);
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
        Assert.Contains("Could not find the channel", followup);
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
        Assert.Contains("Failed to generate a recipe", followup);
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
        Assert.Contains(embed.Fields, f => f.Name == "📊 Nutrition per Serving" && f.Value.Contains("320 kcal"));
        Assert.Contains(embed.Fields, f => f.Name == "🛡️ Safety & Renal Notes" && f.Value.Contains("Vollständig durchgaren."));
        Assert.NotNull(embed.Footer);
        Assert.Contains("@Tester", embed.Footer!.Value.Text);

        var followup = Assert.Single(module.Followups);
        Assert.Contains("Recipe successfully created", followup);
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
        Assert.Contains("unexpected error", followup);
        Assert.Null(module.PostedEmbed);
    }

    [Fact]
    public async Task GetSeasonalProduceAsync_ReportsMissingChannel_WhenFoodChannelIsAbsent()
    {
        var service = new Mock<IGeminiFoodService>();
        var module = CreateModule(service.Object, channel: null);

        await module.GetSeasonalProduceAsync(7);

        var followup = Assert.Single(module.Followups);
        Assert.Contains("Could not find the channel", followup);
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
        Assert.Contains("Invalid month", followup);
        Assert.Null(module.PostedEmbed);
        service.Verify(s => s.GetSeasonalProduceAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetSeasonalProduceAsync_UsesCurrentMonth_WhenMonthOmitted()
    {
        DateTime expected = DateTime.Now;
        var service = new Mock<IGeminiFoodService>();
        service.Setup(s => s.GetSeasonalProduceAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((SeasonalFoodData?)null);
        var module = CreateModule(service.Object, CreateFoodChannel());

        await module.GetSeasonalProduceAsync();

        service.Verify(
            s => s.GetSeasonalProduceAsync(
                It.Is<DateTime>(d => d.Year == expected.Year && d.Month == expected.Month && d.Day == 1),
                It.IsAny<CancellationToken>()),
            Times.Once);

        var followup = Assert.Single(module.Followups);
        Assert.Contains("Failed to retrieve seasonal produce", followup);
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
        Assert.Contains("successfully posted", followup);
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
        Assert.Contains("Failed to retrieve seasonal produce", followup);
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
        Assert.Contains("unexpected error", followup);
        Assert.Null(module.PostedEmbed);
    }
}
