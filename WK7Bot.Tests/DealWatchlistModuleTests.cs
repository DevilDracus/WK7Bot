using System.Reflection;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using WK7Bot.Core.Entities;
using WK7Bot.Modules;
using WK7Bot.Services.Interfaces;
using Xunit;

namespace WK7Bot.Tests;

public class DealWatchlistModuleTests
{
    private sealed class TestDealWatchlistModule : DealWatchlistModule
    {
        public int DeferCount { get; private set; }
        public List<string> Followups { get; } = new();

        public TestDealWatchlistModule(IDealWatchlistService service)
            : base(service, NullLogger<DealWatchlistModule>.Instance)
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
            Followups.Add(text ?? string.Empty);
            return Task.FromResult<IUserMessage>(null!);
        }
    }

    private static TestDealWatchlistModule CreateModule(Mock<IDealWatchlistService> service, ulong userId = 7)
    {
        var module = new TestDealWatchlistModule(service.Object);
        var context = DiscordTestDoubles.CreateContext();
        DiscordTestDoubles.SetUser(context, "Tester", userId);
        ((IInteractionModuleBase)module).SetContext(context);
        return module;
    }

    [Fact]
    public void Constructor_Throws_WhenDependenciesAreNull()
    {
        Assert.Throws<ArgumentNullException>(() => new DealWatchlistModule(null!, NullLogger<DealWatchlistModule>.Instance));
        Assert.Throws<ArgumentNullException>(() => new DealWatchlistModule(new Mock<IDealWatchlistService>().Object, null!));
    }

    [Fact]
    public void WatchCommand_IsExposedAsSlashCommand()
    {
        var attr = typeof(DealWatchlistModule).GetMethod(nameof(DealWatchlistModule.WatchAsync))!
            .GetCustomAttribute<SlashCommandAttribute>();
        Assert.NotNull(attr);
        Assert.Equal("watch", attr!.Name);
    }

    [Fact]
    public void UnwatchCommand_IsExposedAsSlashCommand()
    {
        var attr = typeof(DealWatchlistModule).GetMethod(nameof(DealWatchlistModule.UnwatchAsync))!
            .GetCustomAttribute<SlashCommandAttribute>();
        Assert.NotNull(attr);
        Assert.Equal("unwatch", attr!.Name);
    }

    [Fact]
    public void WatchlistCommand_IsExposedAsSlashCommand()
    {
        var attr = typeof(DealWatchlistModule).GetMethod(nameof(DealWatchlistModule.WatchlistAsync))!
            .GetCustomAttribute<SlashCommandAttribute>();
        Assert.NotNull(attr);
        Assert.Equal("watchlist", attr!.Name);
    }

    [Fact]
    public async Task WatchAsync_ReportsSuccess()
    {
        var service = new Mock<IDealWatchlistService>();
        service.Setup(s => s.WatchAsync(7, "Portal", 10, 5.5m, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DealWatchResult(true, "Portal beobachtet"));
        var module = CreateModule(service);

        await module.WatchAsync("Portal", 10, 5.5);

        Assert.Equal(1, module.DeferCount);
        service.Verify(s => s.WatchAsync(7, "Portal", 10, 5.5m, It.IsAny<CancellationToken>()), Times.Once);
        Assert.Single(module.Followups);
        Assert.StartsWith("✅", module.Followups[0]);
    }

    [Fact]
    public async Task WatchAsync_ReportsFailure()
    {
        var service = new Mock<IDealWatchlistService>();
        service.Setup(s => s.WatchAsync(It.IsAny<ulong>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<decimal?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DealWatchResult(false, "nicht gefunden"));
        var module = CreateModule(service);

        await module.WatchAsync("Nope");

        Assert.Single(module.Followups);
        Assert.StartsWith("❌", module.Followups[0]);
    }

    [Fact]
    public async Task UnwatchAsync_ReportsResult()
    {
        var service = new Mock<IDealWatchlistService>();
        service.Setup(s => s.UnwatchAsync(7, "Portal", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DealWatchResult(true, "entfernt"));
        var module = CreateModule(service);

        await module.UnwatchAsync("Portal");

        Assert.Equal(1, module.DeferCount);
        Assert.Single(module.Followups);
        Assert.StartsWith("✅", module.Followups[0]);
    }

    [Fact]
    public async Task WatchlistAsync_EmptyWatchlist()
    {
        var service = new Mock<IDealWatchlistService>();
        service.Setup(s => s.GetWatchlistAsync(7, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DealWatchItem>());
        var module = CreateModule(service);

        await module.WatchlistAsync();

        Assert.Equal(1, module.DeferCount);
        Assert.Single(module.Followups);
        Assert.Contains("Watchlist ist leer", module.Followups[0]);
    }

    [Fact]
    public async Task WatchlistAsync_ListsItems()
    {
        var service = new Mock<IDealWatchlistService>();
        service.Setup(s => s.GetWatchlistAsync(7, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DealWatchItem>
            {
                new DealWatchItem { GameName = "Portal", MinDiscountPercent = 20, TargetPrice = 5m }
            });
        var module = CreateModule(service);

        await module.WatchlistAsync();

        Assert.Single(module.Followups);
        Assert.Contains("Portal", module.Followups[0]);
    }
}
