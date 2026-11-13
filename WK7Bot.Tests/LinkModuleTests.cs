using System.Reflection;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using WK7Bot.Core.Entities;
using WK7Bot.Core.Interfaces;
using WK7Bot.Modules;
using WK7Bot.Services.Interfaces;
using Xunit;

namespace WK7Bot.Tests;

public class LinkModuleTests
{
    private sealed class TestLinkModule : LinkModule
    {
        public int DeferCount { get; private set; }

        public List<string> Followups { get; } = new();

        public TestLinkModule(
            IAccountLinkService accountLinkService,
            IManualWishlistRepository manualWishlistRepository)
            : base(accountLinkService, manualWishlistRepository, NullLogger<LinkModule>.Instance)
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

    private static TestLinkModule CreateModule(
        IAccountLinkService accountLinkService,
        IManualWishlistRepository? manualWishlistRepository = null,
        ulong userId = 42)
    {
        var module = new TestLinkModule(
            accountLinkService,
            manualWishlistRepository ?? new Mock<IManualWishlistRepository>().Object);

        var context = DiscordTestDoubles.CreateContext();
        DiscordTestDoubles.SetUser(context, "Tester", userId);
        ((IInteractionModuleBase)module).SetContext(context);

        return module;
    }

    private static Mock<IAccountLinkService> CreateAccountLinkService()
        => new();

    private static Mock<IManualWishlistRepository> CreateManualWishlistRepository()
        => new();

    [Fact]
    public void Constructor_Throws_WhenDependenciesAreNull()
    {
        var service = new Mock<IAccountLinkService>().Object;
        var repo = new Mock<IManualWishlistRepository>().Object;

        Assert.Throws<ArgumentNullException>(() => new LinkModule(null!, repo, NullLogger<LinkModule>.Instance));
        Assert.Throws<ArgumentNullException>(() => new LinkModule(service, null!, NullLogger<LinkModule>.Instance));
        Assert.Throws<ArgumentNullException>(() => new LinkModule(service, repo, null!));
    }

    [Theory]
    [InlineData(nameof(LinkModule.LinkSteamAsync), "link-steam")]
    [InlineData(nameof(LinkModule.LinkBattleNetAsync), "link-battlenet")]
    [InlineData(nameof(LinkModule.LinkStatusAsync), "link-status")]
    [InlineData(nameof(LinkModule.LinkRemoveAsync), "link-remove")]
    [InlineData(nameof(LinkModule.WishlistAddAsync), "wishlist-add")]
    [InlineData(nameof(LinkModule.WishlistAddAppIdAsync), "wishlist-add-appid")]
    [InlineData(nameof(LinkModule.WishlistRemoveAppIdAsync), "wishlist-remove-appid")]
    [InlineData(nameof(LinkModule.WishlistListAppIdAsync), "wishlist-list-appid")]
    public void LinkCommands_AreExposedAsSlashCommands(string methodName, string expectedCommandName)
    {
        var method = typeof(LinkModule).GetMethod(methodName)!;
        var attribute = method.GetCustomAttribute<SlashCommandAttribute>();

        Assert.NotNull(attribute);
        Assert.Equal(expectedCommandName, attribute!.Name);
        Assert.False(string.IsNullOrWhiteSpace(attribute.Description));
    }

    [Fact]
    public async Task LinkSteamAsync_ReturnsSuccessMessage_WhenServiceSucceeds()
    {
        var service = CreateAccountLinkService();
        service.Setup(s => s.LinkSteamAsync(42ul, "https://steamcommunity.com/id/test", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AccountLinkResult(true, "Account verknüpft.", "12345"));
        var module = CreateModule(service.Object);

        await module.LinkSteamAsync("https://steamcommunity.com/id/test");

        Assert.Equal(1, module.DeferCount);
        var followup = Assert.Single(module.Followups);
        Assert.StartsWith("✅", followup);
        Assert.Contains("Account verknüpft", followup);
    }

    [Fact]
    public async Task LinkSteamAsync_ReturnsErrorMessage_WhenServiceFails()
    {
        var service = CreateAccountLinkService();
        service.Setup(s => s.LinkSteamAsync(42ul, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AccountLinkResult(false, "Profil nicht gefunden."));
        var module = CreateModule(service.Object);

        await module.LinkSteamAsync("nonsense");

        var followup = Assert.Single(module.Followups);
        Assert.StartsWith("❌", followup);
        Assert.Contains("Profil nicht gefunden", followup);
    }

    [Fact]
    public async Task LinkBattleNetAsync_ReturnsSuccessMessage_WhenServiceSucceeds()
    {
        var service = CreateAccountLinkService();
        service.Setup(s => s.LinkBattleNetAsync(42ul, "token", "eu", "Tester#1234", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AccountLinkResult(true, "Battle.net verknüpft.", "Tester#1234"));
        var module = CreateModule(service.Object);

        await module.LinkBattleNetAsync("token", "eu", "Tester#1234");

        Assert.Equal(1, module.DeferCount);
        var followup = Assert.Single(module.Followups);
        Assert.StartsWith("✅", followup);
    }

    [Fact]
    public async Task LinkStatusAsync_ReportsNoLinks()
    {
        var service = CreateAccountLinkService();
        service.Setup(s => s.GetLinksAsync(42ul, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<UserAccountLink>());
        var module = CreateModule(service.Object);

        await module.LinkStatusAsync();

        var followup = Assert.Single(module.Followups);
        Assert.Contains("noch keine Accounts", followup);
    }

    [Fact]
    public async Task LinkStatusAsync_ListsLinks()
    {
        var service = CreateAccountLinkService();
        service.Setup(s => s.GetLinksAsync(42ul, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<UserAccountLink>
            {
                new() { Provider = "steam", ExternalId = "12345", WishlistUrl = "https://store.steampowered.com/wishlist/id/test" }
            });
        var module = CreateModule(service.Object);

        await module.LinkStatusAsync();

        var followup = Assert.Single(module.Followups);
        Assert.Contains("steam", followup);
        Assert.Contains("12345", followup);
        Assert.Contains("Wishlist", followup);
    }

    [Fact]
    public async Task LinkRemoveAsync_ReturnsResultMessage()
    {
        var service = CreateAccountLinkService();
        service.Setup(s => s.UnlinkAsync(42ul, "steam", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AccountLinkResult(true, "Verknüpfung entfernt."));
        var module = CreateModule(service.Object);

        await module.LinkRemoveAsync("steam");

        var followup = Assert.Single(module.Followups);
        Assert.StartsWith("✅", followup);
    }

    [Fact]
    public async Task WishlistAddAsync_ReturnsSuccessMessage()
    {
        var service = CreateAccountLinkService();
        service.Setup(s => s.LinkWishlistAsync(42ul, "https://store.steampowered.com/wishlist/id/test", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AccountLinkResult(true, "Wunschliste verknüpft."));
        var module = CreateModule(service.Object);

        await module.WishlistAddAsync("https://store.steampowered.com/wishlist/id/test");

        var followup = Assert.Single(module.Followups);
        Assert.StartsWith("✅", followup);
    }

    [Fact]
    public async Task WishlistAddAppIdAsync_AddsItem_WhenNotDuplicate()
    {
        var service = CreateAccountLinkService();
        var repo = CreateManualWishlistRepository();
        repo.Setup(r => r.GetForUserAsync(42ul, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ManualWishlistItem>());
        var module = CreateModule(service.Object, repo.Object);

        await module.WishlistAddAppIdAsync(12345, "Cool Game");

        repo.Verify(r => r.AddAsync(It.Is<ManualWishlistItem>(i => i.AppId == 12345 && i.Name == "Cool Game" && i.DiscordUserId == 42ul), It.IsAny<CancellationToken>()), Times.Once);
        var followup = Assert.Single(module.Followups);
        Assert.StartsWith("✅", followup);
    }

    [Fact]
    public async Task WishlistAddAppIdAsync_RejectsDuplicate()
    {
        var service = CreateAccountLinkService();
        var repo = CreateManualWishlistRepository();
        repo.Setup(r => r.GetForUserAsync(42ul, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ManualWishlistItem> { new() { AppId = 12345, Name = "Cool Game" } });
        var module = CreateModule(service.Object, repo.Object);

        await module.WishlistAddAppIdAsync(12345);

        repo.Verify(r => r.AddAsync(It.IsAny<ManualWishlistItem>(), It.IsAny<CancellationToken>()), Times.Never);
        var followup = Assert.Single(module.Followups);
        Assert.StartsWith("❌", followup);
    }

    [Fact]
    public async Task WishlistRemoveAppIdAsync_RemovesExisting()
    {
        var service = CreateAccountLinkService();
        var repo = CreateManualWishlistRepository();
        repo.Setup(r => r.RemoveAsync(42ul, 12345u, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var module = CreateModule(service.Object, repo.Object);

        await module.WishlistRemoveAppIdAsync(12345);

        var followup = Assert.Single(module.Followups);
        Assert.StartsWith("✅", followup);
    }

    [Fact]
    public async Task WishlistRemoveAppIdAsync_ReportsMissing()
    {
        var service = CreateAccountLinkService();
        var repo = CreateManualWishlistRepository();
        repo.Setup(r => r.RemoveAsync(42ul, 999u, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        var module = CreateModule(service.Object, repo.Object);

        await module.WishlistRemoveAppIdAsync(999);

        var followup = Assert.Single(module.Followups);
        Assert.StartsWith("❌", followup);
    }

    [Fact]
    public async Task WishlistListAppIdAsync_ReportsEmpty()
    {
        var service = CreateAccountLinkService();
        var repo = CreateManualWishlistRepository();
        repo.Setup(r => r.GetForUserAsync(42ul, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ManualWishlistItem>());
        var module = CreateModule(service.Object, repo.Object);

        await module.WishlistListAppIdAsync();

        var followup = Assert.Single(module.Followups);
        Assert.Contains("leer", followup);
    }

    [Fact]
    public async Task WishlistListAppIdAsync_ListsItems()
    {
        var service = CreateAccountLinkService();
        var repo = CreateManualWishlistRepository();
        repo.Setup(r => r.GetForUserAsync(42ul, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ManualWishlistItem>
            {
                new() { AppId = 12345, Name = "Cool Game" }
            });
        var module = CreateModule(service.Object, repo.Object);

        await module.WishlistListAppIdAsync();

        var followup = Assert.Single(module.Followups);
        Assert.Contains("Cool Game", followup);
        Assert.Contains("12345", followup);
    }
}
