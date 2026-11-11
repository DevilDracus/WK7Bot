using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using WK7Bot.Core.Entities;
using WK7Bot.Core.Interfaces;
using WK7Bot.Core.Utilities;
using WK7Bot.Models;
using WK7Bot.Options;
using WK7Bot.Services;
using WK7Bot.Services.Interfaces;
using Xunit;

namespace WK7Bot.Tests;

public class WishlistDealBackgroundServiceTests
{
    private sealed class TestableWishlistDealBackgroundService : WishlistDealBackgroundService
    {
        public TestableWishlistDealBackgroundService(
            IServiceScopeFactory scopeFactory,
            DiscordSocketClient discordClient,
            IWishlistDealService dealService,
            ISteamService steamService,
            IManualWishlistRepository manualWishlistRepository,
            IFeatureHealthTracker health,
            IOptions<Wk7BotOptions> options,
            FeatureGate featureGate)
            : base(scopeFactory, discordClient, dealService, steamService, manualWishlistRepository, health, options, featureGate, NullLogger<WishlistDealBackgroundService>.Instance)
        {
        }

        public Task TestMergeManualWishlistItemsAsync(
            Dictionary<ulong, List<SteamWishlistItem>> wishlistsByDiscordId,
            HashSet<uint> allAppIds,
            CancellationToken cancellationToken = default)
            => MergeManualWishlistItemsAsync(wishlistsByDiscordId, allAppIds, cancellationToken);
    }

    private static TestableWishlistDealBackgroundService CreateService(IManualWishlistRepository manualRepository)
    {
        var options = Microsoft.Extensions.Options.Options.Create(new Wk7BotOptions());
        return new TestableWishlistDealBackgroundService(
            Mock.Of<IServiceScopeFactory>(),
            (DiscordSocketClient)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(DiscordSocketClient)),
            Mock.Of<IWishlistDealService>(),
            Mock.Of<ISteamService>(),
            manualRepository,
            Mock.Of<IFeatureHealthTracker>(),
            options,
            new FeatureGate(options));
    }

    [Fact]
    public async Task MergeManualWishlistItemsAsync_AddsItemsForUsersWithoutWishlist()
    {
        var manualRepo = new Mock<IManualWishlistRepository>();
        manualRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ManualWishlistItem>
            {
                new() { DiscordUserId = 100, AppId = 123, Name = "Manual Game" }
            });

        var service = CreateService(manualRepo.Object);
        var wishlists = new Dictionary<ulong, List<SteamWishlistItem>>();
        var allAppIds = new HashSet<uint>();

        await service.TestMergeManualWishlistItemsAsync(wishlists, allAppIds);

        var items = Assert.Single(wishlists);
        Assert.Equal(100ul, items.Key);
        var item = Assert.Single(items.Value);
        Assert.Equal(123u, item.AppId);
        Assert.Equal("Manual Game", item.Name);
        Assert.Contains(123u, allAppIds);
    }

    [Fact]
    public async Task MergeManualWishlistItemsAsync_MergesWithExistingWishlist()
    {
        var manualRepo = new Mock<IManualWishlistRepository>();
        manualRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ManualWishlistItem>
            {
                new() { DiscordUserId = 100, AppId = 789, Name = "Extra" }
            });

        var service = CreateService(manualRepo.Object);
        var wishlists = new Dictionary<ulong, List<SteamWishlistItem>>
        {
            [100] = new() { new SteamWishlistItem { AppId = 456, Name = "Wishlist Game" } }
        };
        var allAppIds = new HashSet<uint> { 456 };

        await service.TestMergeManualWishlistItemsAsync(wishlists, allAppIds);

        Assert.Equal(2, wishlists[100].Count);
        Assert.Contains(456u, allAppIds);
        Assert.Contains(789u, allAppIds);
    }

    [Fact]
    public async Task MergeManualWishlistItemsAsync_SkipsDuplicateAppIds()
    {
        var manualRepo = new Mock<IManualWishlistRepository>();
        manualRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ManualWishlistItem>
            {
                new() { DiscordUserId = 100, AppId = 456, Name = "Duplicate" }
            });

        var service = CreateService(manualRepo.Object);
        var wishlists = new Dictionary<ulong, List<SteamWishlistItem>>
        {
            [100] = new() { new SteamWishlistItem { AppId = 456, Name = "Wishlist Game" } }
        };
        var allAppIds = new HashSet<uint> { 456 };

        await service.TestMergeManualWishlistItemsAsync(wishlists, allAppIds);

        Assert.Single(wishlists[100]);
    }

    [Fact]
    public async Task MergeManualWishlistItemsAsync_FallsBackToAppIdPlaceholder_WhenNameIsEmpty()
    {
        var manualRepo = new Mock<IManualWishlistRepository>();
        manualRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ManualWishlistItem>
            {
                new() { DiscordUserId = 100, AppId = 999, Name = "" }
            });

        var service = CreateService(manualRepo.Object);
        var wishlists = new Dictionary<ulong, List<SteamWishlistItem>>();
        var allAppIds = new HashSet<uint>();

        await service.TestMergeManualWishlistItemsAsync(wishlists, allAppIds);

        var item = Assert.Single(Assert.Single(wishlists).Value);
        Assert.Equal("AppID 999", item.Name);
    }
}
