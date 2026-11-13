using System.Reflection;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using WK7Bot.Core.Entities;
using WK7Bot.Core.Interfaces;
using WK7Bot.Models;
using WK7Bot.Modules;
using WK7Bot.Services.Interfaces;
using Xunit;

namespace WK7Bot.Tests;

public class PriceHistoryModuleTests
{
    private sealed class TestPriceHistoryModule : PriceHistoryModule
    {
        public int DeferCount { get; private set; }

        public List<string> Followups { get; } = new();

        public List<Embed> Embeds { get; } = new();

        public TestPriceHistoryModule(
            IPriceHistoryRepository priceHistoryRepository,
            ISteamStoreService steamStoreService,
            IWishlistDealService dealService)
            : base(priceHistoryRepository, steamStoreService, dealService, NullLogger<PriceHistoryModule>.Instance)
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

    private static TestPriceHistoryModule CreateModule(
        IPriceHistoryRepository priceHistoryRepository,
        ISteamStoreService? steamStoreService = null,
        IWishlistDealService? dealService = null)
    {
        var module = new TestPriceHistoryModule(
            priceHistoryRepository,
            steamStoreService ?? new Mock<ISteamStoreService>().Object,
            dealService ?? new Mock<IWishlistDealService>().Object);
        var context = DiscordTestDoubles.CreateContext();
        DiscordTestDoubles.SetUser(context, "Tester", 42);
        ((IInteractionModuleBase)module).SetContext(context);
        return module;
    }

    private static List<PriceHistorySnapshot> CreateSnapshots(int count = 5)
        => Enumerable.Range(0, count)
            .Select(i => new PriceHistorySnapshot
            {
                AppId = 12345,
                Name = "Cool Game",
                Store = "Steam",
                Price = 20m - i,
                RegularPrice = 40m,
                DiscountPercent = (int)((1 - (20m - i) / 40m) * 100),
                RecordedAt = DateTime.UtcNow.AddDays(-count + i)
            })
            .ToList();

    [Fact]
    public void Constructor_Throws_WhenDependenciesAreNull()
    {
        var repo = new Mock<IPriceHistoryRepository>().Object;
        var steam = new Mock<ISteamStoreService>().Object;
        var deal = new Mock<IWishlistDealService>().Object;

        Assert.Throws<ArgumentNullException>(() => new PriceHistoryModule(null!, steam, deal, NullLogger<PriceHistoryModule>.Instance));
        Assert.Throws<ArgumentNullException>(() => new PriceHistoryModule(repo, null!, deal, NullLogger<PriceHistoryModule>.Instance));
        Assert.Throws<ArgumentNullException>(() => new PriceHistoryModule(repo, steam, null!, NullLogger<PriceHistoryModule>.Instance));
        Assert.Throws<ArgumentNullException>(() => new PriceHistoryModule(repo, steam, deal, null!));
    }

    [Fact]
    public void PriceHistoryCommand_IsExposedAsSlashCommand()
    {
        var method = typeof(PriceHistoryModule).GetMethod(nameof(PriceHistoryModule.PriceHistoryAsync))!;
        var attribute = method.GetCustomAttribute<SlashCommandAttribute>();

        Assert.NotNull(attribute);
        Assert.Equal("price-history", attribute!.Name);
        Assert.Contains("Preisverlauf", attribute.Description);

        Assert.Equal("game", method.GetParameters()[0].GetCustomAttribute<SummaryAttribute>()?.Name);
    }

    [Fact]
    public async Task PriceHistoryAsync_ResolvesAppId_WhenNumericInput()
    {
        var repo = new Mock<IPriceHistoryRepository>();
        repo.Setup(r => r.GetSnapshotsAsync(12345u, It.IsAny<DateTime>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSnapshots());
        var module = CreateModule(repo.Object);

        await module.PriceHistoryAsync("12345");

        Assert.Equal(1, module.DeferCount);
        Assert.Single(module.Embeds);
    }

    [Fact]
    public async Task PriceHistoryAsync_SearchesByName_WhenTextInput()
    {
        var steamService = new Mock<ISteamStoreService>();
        steamService.Setup(s => s.SearchGameAsync("Cool Game", It.IsAny<CancellationToken>()))
            .ReturnsAsync((12345u, "Cool Game"));
        var repo = new Mock<IPriceHistoryRepository>();
        repo.Setup(r => r.GetSnapshotsAsync(12345u, It.IsAny<DateTime>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSnapshots());
        var module = CreateModule(repo.Object, steamService.Object);

        await module.PriceHistoryAsync("Cool Game");

        Assert.Equal(1, module.DeferCount);
        Assert.Single(module.Embeds);
        steamService.Verify(s => s.SearchGameAsync("Cool Game", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PriceHistoryAsync_ReportsNotFound_WhenSearchReturnsNull()
    {
        var steamService = new Mock<ISteamStoreService>();
        steamService.Setup(s => s.SearchGameAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(((uint AppId, string Title)?)null);
        var module = CreateModule(new Mock<IPriceHistoryRepository>().Object, steamService.Object);

        await module.PriceHistoryAsync("Unknown Game");

        Assert.Equal(1, module.DeferCount);
        var followup = Assert.Single(module.Followups);
        Assert.Contains("Kein Spiel", followup);
        Assert.Empty(module.Embeds);
    }

    [Fact]
    public async Task PriceHistoryAsync_ReportsMissingData_WhenLessThanTwoSnapshots_AndCurrentPriceAvailable()
    {
        var repo = new Mock<IPriceHistoryRepository>();
        repo.Setup(r => r.GetSnapshotsAsync(12345u, It.IsAny<DateTime>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PriceHistorySnapshot> { CreateSnapshots(1)[0] });
        var dealService = new Mock<IWishlistDealService>();
        dealService.Setup(s => s.FetchPricesAsync(It.Is<IEnumerable<uint>>(x => x.Contains(12345u)), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<uint, ItadPriceData>
            {
                [12345] = new() { Title = "Cool Game", CurrentPrice = 19.99m, StoreName = "Steam" }
            });
        var module = CreateModule(repo.Object, dealService: dealService.Object);

        await module.PriceHistoryAsync("12345");

        Assert.Equal(1, module.DeferCount);
        var followup = Assert.Single(module.Followups);
        Assert.Contains("Cool Game", followup);
        Assert.Contains("Noch nicht genug", followup);
        Assert.Empty(module.Embeds);
    }

    [Fact]
    public async Task PriceHistoryAsync_ReportsMissingData_WhenLessThanTwoSnapshots_AndCurrentPriceUnavailable()
    {
        var repo = new Mock<IPriceHistoryRepository>();
        repo.Setup(r => r.GetSnapshotsAsync(12345u, It.IsAny<DateTime>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PriceHistorySnapshot> { CreateSnapshots(1)[0] });
        var steamService = new Mock<ISteamStoreService>();
        steamService.Setup(s => s.SearchGameAsync("Cool Game", It.IsAny<CancellationToken>()))
            .ReturnsAsync((12345u, "Cool Game"));
        var dealService = new Mock<IWishlistDealService>();
        dealService.Setup(s => s.FetchPricesAsync(It.IsAny<IEnumerable<uint>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<uint, ItadPriceData>());
        var module = CreateModule(repo.Object, steamService.Object, dealService.Object);

        await module.PriceHistoryAsync("Cool Game");

        var followup = Assert.Single(module.Followups);
        Assert.Contains("noch keine Verlaufsdaten", followup);
    }

    [Fact]
    public async Task PriceHistoryAsync_PostsChartEmbed_WhenEnoughSnapshots()
    {
        var repo = new Mock<IPriceHistoryRepository>();
        repo.Setup(r => r.GetSnapshotsAsync(12345u, It.IsAny<DateTime>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSnapshots(5));
        var module = CreateModule(repo.Object);

        await module.PriceHistoryAsync("12345");

        Assert.Equal(1, module.DeferCount);
        var embed = Assert.Single(module.Embeds);
        Assert.Contains("Preisverlauf", embed.Title);
        Assert.Contains("Aktuell", embed.Fields.Select(f => f.Name));
        Assert.Contains($"Bestpreis (30 Tage)", embed.Fields.Select(f => f.Name));
        Assert.Contains("https://store.steampowered.com/app/12345", embed.Url);
    }

    [Fact]
    public async Task PriceHistoryAsync_ReportsError_WhenServiceThrows()
    {
        var repo = new Mock<IPriceHistoryRepository>();
        repo.Setup(r => r.GetSnapshotsAsync(It.IsAny<uint>(), It.IsAny<DateTime>(), null, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("db down"));
        var module = CreateModule(repo.Object);

        await module.PriceHistoryAsync("12345");

        Assert.Equal(1, module.DeferCount);
        var followup = Assert.Single(module.Followups);
        Assert.Contains("konnte nicht geladen werden", followup);
    }
}
