using System.Reflection;
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

public class PriceComparisonModuleTests
{
    private sealed class TestPriceComparisonModule : PriceComparisonModule
    {
        public int DeferCount { get; private set; }

        public List<string> Followups { get; } = new();

        public List<Embed> Embeds { get; } = new();

        public TestPriceComparisonModule(
            IWishlistDealService dealService,
            ISteamStoreService steamStoreService)
            : base(dealService, steamStoreService, NullLogger<PriceComparisonModule>.Instance)
        {
        }

        protected override Task DeferAsync(bool ephemeral, RequestOptions options)
        {
            DeferCount++;
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
            PollProperties? poll = null,
            MessageFlags flags = MessageFlags.None)
        {
            if (text != null)
            {
                Followups.Add(text);
            }

            if (embed != null)
            {
                Embeds.Add(embed);
            }

            return Task.FromResult<IUserMessage>(null!);
        }
    }

    private static TestPriceComparisonModule CreateModule(
        IWishlistDealService dealService,
        ISteamStoreService? steamStoreService = null)
    {
        var module = new TestPriceComparisonModule(
            dealService,
            steamStoreService ?? new Mock<ISteamStoreService>().Object);
        var context = DiscordTestDoubles.CreateContext();
        DiscordTestDoubles.SetUser(context, "Tester", 42);
        ((IInteractionModuleBase)module).SetContext(context);
        return module;
    }

    private static PriceComparisonResult CreateComparison(uint appId = 12345)
        => new()
        {
            Title = "Cool Game",
            AppId = appId,
            Offers = new List<StorePriceOffer>
            {
                new()
                {
                    StoreName = "Steam",
                    Price = 19.99m,
                    RegularPrice = 39.99m,
                    DiscountPercent = 50,
                    DealUrl = "https://example.com/steam"
                },
                new()
                {
                    StoreName = "GOG",
                    Price = 23.99m,
                    RegularPrice = 39.99m,
                    DiscountPercent = 40,
                    DealUrl = "https://example.com/gog"
                }
            }
        };

    [Fact]
    public void Constructor_Throws_WhenDependenciesAreNull()
    {
        var dealService = new Mock<IWishlistDealService>().Object;
        var steamService = new Mock<ISteamStoreService>().Object;

        Assert.Throws<ArgumentNullException>(() => new PriceComparisonModule(null!, steamService, NullLogger<PriceComparisonModule>.Instance));
        Assert.Throws<ArgumentNullException>(() => new PriceComparisonModule(dealService, null!, NullLogger<PriceComparisonModule>.Instance));
        Assert.Throws<ArgumentNullException>(() => new PriceComparisonModule(dealService, steamService, null!));
    }

    [Fact]
    public void ComparePricesCommand_IsExposedAsSlashCommand()
    {
        var method = typeof(PriceComparisonModule).GetMethod(nameof(PriceComparisonModule.ComparePricesAsync))!;
        var attribute = method.GetCustomAttribute<SlashCommandAttribute>();

        Assert.NotNull(attribute);
        Assert.Equal("compare-prices", attribute!.Name);
        Assert.Contains("Preise", attribute.Description);

        Assert.Equal("game", method.GetParameters()[0].GetCustomAttribute<SummaryAttribute>()?.Name);
    }

    [Fact]
    public async Task ComparePricesAsync_ResolvesAppId_WhenNumericInput()
    {
        var dealService = new Mock<IWishlistDealService>();
        dealService.Setup(s => s.ComparePricesAsync(12345u, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateComparison(12345));
        var module = CreateModule(dealService.Object);

        await module.ComparePricesAsync("12345");

        Assert.Equal(1, module.DeferCount);
        var embed = Assert.Single(module.Embeds);
        Assert.Contains("Cool Game", embed.Title);
        dealService.Verify(s => s.ComparePricesAsync(12345u, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ComparePricesAsync_SearchesByName_WhenTextInput()
    {
        var steamService = new Mock<ISteamStoreService>();
        steamService.Setup(s => s.SearchGameAsync("Cool Game", It.IsAny<CancellationToken>()))
            .ReturnsAsync((12345u, "Cool Game"));
        var dealService = new Mock<IWishlistDealService>();
        dealService.Setup(s => s.ComparePricesAsync(12345u, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateComparison(12345));
        var module = CreateModule(dealService.Object, steamService.Object);

        await module.ComparePricesAsync("Cool Game");

        Assert.Equal(1, module.DeferCount);
        Assert.Single(module.Embeds);
        steamService.Verify(s => s.SearchGameAsync("Cool Game", It.IsAny<CancellationToken>()), Times.Once);
        dealService.Verify(s => s.ComparePricesAsync(12345u, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ComparePricesAsync_ReportsNotFound_WhenSearchReturnsNull()
    {
        var steamService = new Mock<ISteamStoreService>();
        steamService.Setup(s => s.SearchGameAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(((uint AppId, string Title)?)null);
        var module = CreateModule(new Mock<IWishlistDealService>().Object, steamService.Object);

        await module.ComparePricesAsync("Unknown Game");

        Assert.Equal(1, module.DeferCount);
        var followup = Assert.Single(module.Followups);
        Assert.Contains("Kein Spiel", followup);
        Assert.Empty(module.Embeds);
    }

    [Fact]
    public async Task ComparePricesAsync_ReportsNoOffers_WhenComparisonEmpty()
    {
        var dealService = new Mock<IWishlistDealService>();
        dealService.Setup(s => s.ComparePricesAsync(It.IsAny<uint>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PriceComparisonResult { Title = "Cool Game", AppId = 12345, Offers = new List<StorePriceOffer>() });
        var module = CreateModule(dealService.Object);

        await module.ComparePricesAsync("12345");

        var followup = Assert.Single(module.Followups);
        Assert.Contains("Keine Preisdaten", followup);
        Assert.Empty(module.Embeds);
    }

    [Fact]
    public async Task ComparePricesAsync_PostsEmbed_WhenOffersExist()
    {
        var dealService = new Mock<IWishlistDealService>();
        dealService.Setup(s => s.ComparePricesAsync(12345u, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateComparison(12345));
        var module = CreateModule(dealService.Object);

        await module.ComparePricesAsync("12345");

        var embed = Assert.Single(module.Embeds);
        Assert.Contains("Cool Game", embed.Title);
        Assert.Contains("Steam", embed.Description);
        Assert.Contains("GOG", embed.Description);
        Assert.Equal($"https://store.steampowered.com/app/12345", embed.Url);
        Assert.Equal(Color.Green, embed.Color);
    }

    [Fact]
    public async Task ComparePricesAsync_ReportsError_WhenServiceThrows()
    {
        var dealService = new Mock<IWishlistDealService>();
        dealService.Setup(s => s.ComparePricesAsync(It.IsAny<uint>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("service down"));
        var module = CreateModule(dealService.Object);

        await module.ComparePricesAsync("12345");

        var followup = Assert.Single(module.Followups);
        Assert.Contains("fehlgeschlagen", followup);
    }
}
