using System.Reflection;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using WK7Bot.Core.Utilities;
using WK7Bot.Models;
using WK7Bot.Modules;
using WK7Bot.Services.Interfaces;
using Xunit;

namespace WK7Bot.Tests;

public class DealsModuleTests
{
    private sealed class TestDealsModule : DealsModule
    {
        public int DeferCount { get; private set; }
        public int RespondCount { get; private set; }
        public List<string> Followups { get; } = new();
        public List<MessageComponent> FollowupComponents { get; } = new();

        public TestDealsModule(
            IWishlistDealService dealService,
            ISteamService steamService,
            IAccountLinkResolver accountLinkResolver)
            : base(dealService, steamService, accountLinkResolver, new CommandRateLimiter(), NullLogger<DealsModule>.Instance)
        {
        }

        protected override Task DeferAsync(bool ephemeral, RequestOptions options)
        {
            DeferCount++;
            return Task.CompletedTask;
        }

        protected override Task RespondAsync(
            string? text = null,
            Embed[]? embeds = null,
            bool isTTS = false,
            bool ephemeral = false,
            AllowedMentions? allowedMentions = null,
            RequestOptions? options = null,
            MessageComponent? components = null,
            Embed? embed = null,
            PollProperties? poll = null,
            MessageFlags flags = MessageFlags.None)
        {
            RespondCount++;
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
            Followups.Add(text ?? string.Empty);
            if (components != null) FollowupComponents.Add(components);
            return Task.FromResult<IUserMessage>(null!);
        }
    }

    private static TestDealsModule CreateModule(
        Mock<IWishlistDealService>? dealService = null,
        Mock<ISteamService>? steamService = null,
        Mock<IAccountLinkResolver>? accountLinkResolver = null,
        ulong userId = 42)
    {
        var module = new TestDealsModule(
            dealService?.Object ?? Mock.Of<IWishlistDealService>(),
            steamService?.Object ?? Mock.Of<ISteamService>(),
            accountLinkResolver?.Object ?? Mock.Of<IAccountLinkResolver>());
        var context = DiscordTestDoubles.CreateContext();
        DiscordTestDoubles.SetUser(context, "Tester", userId);
        ((IInteractionModuleBase)module).SetContext(context);
        return module;
    }

    [Fact]
    public void Constructor_Throws_WhenDependenciesAreNull()
    {
        var dealService = Mock.Of<IWishlistDealService>();
        var steamService = Mock.Of<ISteamService>();
        var accountLinkResolver = Mock.Of<IAccountLinkResolver>();
        var rateLimiter = new CommandRateLimiter();
        Assert.Throws<ArgumentNullException>(() => new DealsModule(null!, steamService, accountLinkResolver, rateLimiter, NullLogger<DealsModule>.Instance));
        Assert.Throws<ArgumentNullException>(() => new DealsModule(dealService, null!, accountLinkResolver, rateLimiter, NullLogger<DealsModule>.Instance));
        Assert.Throws<ArgumentNullException>(() => new DealsModule(dealService, steamService, null!, rateLimiter, NullLogger<DealsModule>.Instance));
        Assert.Throws<ArgumentNullException>(() => new DealsModule(dealService, steamService, accountLinkResolver, null!, NullLogger<DealsModule>.Instance));
        Assert.Throws<ArgumentNullException>(() => new DealsModule(dealService, steamService, accountLinkResolver, rateLimiter, null!));
    }

    [Fact]
    public void DealsCommand_IsExposedAsSlashCommand()
    {
        var attr = typeof(DealsModule).GetMethod(nameof(DealsModule.GetDealsAsync))!
            .GetCustomAttribute<SlashCommandAttribute>();
        Assert.NotNull(attr);
        Assert.Equal("deals", attr!.Name);
    }

    [Fact]
    public async Task GetDealsAsync_RateLimitsImmediateSecondCall()
    {
        var dealService = new Mock<IWishlistDealService>();
        var steamService = new Mock<ISteamService>();
        steamService.Setup(s => s.GetSteamIdForDiscordUser(It.IsAny<string>())).Returns("1");
        dealService.Setup(s => s.FetchWishlistAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(new List<SteamWishlistItem>());
        var module = CreateModule(dealService, steamService);

        await module.GetDealsAsync();
        await module.GetDealsAsync();

        Assert.Equal(1, module.DeferCount);
        Assert.Equal(1, module.RespondCount);
        dealService.Verify(s => s.FetchWishlistAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetDealsAsync_ReportsMissingSteamLink()
    {
        var steamService = new Mock<ISteamService>();
        steamService.Setup(s => s.GetSteamIdForDiscordUser(It.IsAny<string>())).Returns((string?)null);
        var resolver = new Mock<IAccountLinkResolver>();
        resolver.Setup(r => r.GetSteamIdAsync(It.IsAny<ulong>(), It.IsAny<CancellationToken>())).ReturnsAsync((string?)null);
        var module = CreateModule(steamService: steamService, accountLinkResolver: resolver);

        await module.GetDealsAsync();

        Assert.Equal(1, module.DeferCount);
        Assert.Single(module.Followups);
        Assert.Contains("keinen Steam-Account", module.Followups[0]);
    }

    [Fact]
    public async Task GetDealsAsync_ReportsEmptyWishlist()
    {
        var steamService = new Mock<ISteamService>();
        steamService.Setup(s => s.GetSteamIdForDiscordUser(It.IsAny<string>())).Returns("1");
        var dealService = new Mock<IWishlistDealService>();
        dealService.Setup(s => s.FetchWishlistAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(new List<SteamWishlistItem>());
        var module = CreateModule(dealService, steamService);

        await module.GetDealsAsync();

        Assert.Single(module.Followups);
        Assert.Contains("Wunschliste ist leer", module.Followups[0]);
    }

    [Fact]
    public async Task GetDealsAsync_ReportsNoPrices()
    {
        var steamService = new Mock<ISteamService>();
        steamService.Setup(s => s.GetSteamIdForDiscordUser(It.IsAny<string>())).Returns("1");
        var dealService = new Mock<IWishlistDealService>();
        dealService.Setup(s => s.FetchWishlistAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SteamWishlistItem> { new SteamWishlistItem { AppId = 1, Name = "Game" } });
        dealService.Setup(s => s.FetchPricesAsync(It.IsAny<IEnumerable<uint>>(), It.IsAny<CancellationToken>())).ReturnsAsync(new Dictionary<uint, ItadPriceData>());
        var module = CreateModule(dealService, steamService);

        await module.GetDealsAsync();

        Assert.Single(module.Followups);
        Assert.Contains("Keine Preisdaten", module.Followups[0]);
    }

    [Fact]
    public async Task GetDealsAsync_ReturnsDeals_WhenDataAvailable()
    {
        var steamService = new Mock<ISteamService>();
        steamService.Setup(s => s.GetSteamIdForDiscordUser(It.IsAny<string>())).Returns("1");
        var dealService = new Mock<IWishlistDealService>();
        dealService.Setup(s => s.FetchWishlistAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SteamWishlistItem> { new SteamWishlistItem { AppId = 1, Name = "Game" } });
        dealService.Setup(s => s.FetchPricesAsync(It.IsAny<IEnumerable<uint>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<uint, ItadPriceData>
            {
                [1] = new ItadPriceData { CurrentPrice = 9.99m, RegularPrice = 19.99m, StoreName = "Steam", DealUrl = "http://deal" }
            });
        var module = CreateModule(dealService, steamService);

        await module.GetDealsAsync();

        Assert.Single(module.FollowupComponents);
    }

    [Fact]
    public async Task GetDealsAsync_ReportsError_WhenServiceThrows()
    {
        var steamService = new Mock<ISteamService>();
        steamService.Setup(s => s.GetSteamIdForDiscordUser(It.IsAny<string>())).Returns("1");
        var dealService = new Mock<IWishlistDealService>();
        dealService.Setup(s => s.FetchWishlistAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("service down"));
        var module = CreateModule(dealService, steamService);

        await module.GetDealsAsync();

        Assert.Single(module.Followups);
        Assert.Contains("unerwarteter Fehler", module.Followups[0]);
    }
}
