using System.Reflection;
using System.Runtime.CompilerServices;
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

public class ReleaseDateReminderModuleTests
{
    private sealed class TestModule : ReleaseDateReminderModule
    {
        public int DeferCount { get; private set; }
        public List<string> Followups { get; } = new();

        public TestModule(IReleaseDateReminderRepository repository, ISteamStoreService steamStoreService)
            : base(repository, steamStoreService, NullLogger<ReleaseDateReminderModule>.Instance) { }

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

    private static TestModule CreateModule(
        Mock<IReleaseDateReminderRepository> repository,
        Mock<ISteamStoreService> steamStore,
        ulong userId = 42)
    {
        var module = new TestModule(repository.Object, steamStore.Object);
        var context = DiscordTestDoubles.CreateContext();
        DiscordTestDoubles.SetUser(context, "tester", userId);
        ((IInteractionModuleBase)module).SetContext(context);
        return module;
    }

    [Fact]
    public async Task ReleaseRemindAsync_NoGameFound_ReportsError()
    {
        var repository = new Mock<IReleaseDateReminderRepository>();
        var steamStore = new Mock<ISteamStoreService>();
        steamStore.Setup(s => s.SearchGameAsync("unknown", It.IsAny<CancellationToken>()))
            .ReturnsAsync((ValueTuple<uint, string>?)null);

        var module = CreateModule(repository, steamStore);

        await module.ReleaseRemindAsync("unknown");

        Assert.Equal(1, module.DeferCount);
        Assert.Contains("Kein Spiel", string.Join(" ", module.Followups));
    }

    [Fact]
    public async Task ReleaseRemindAsync_AppIdNotFound_ReportsError()
    {
        var repository = new Mock<IReleaseDateReminderRepository>();
        var steamStore = new Mock<ISteamStoreService>();
        steamStore.Setup(s => s.GetAppDetailsAsync(999u, It.IsAny<CancellationToken>()))
            .ReturnsAsync((SteamStoreAppDetails?)null);

        var module = CreateModule(repository, steamStore);

        await module.ReleaseRemindAsync("999");

        Assert.Contains("Konnte keine Store-Details", string.Join(" ", module.Followups));
    }

    [Fact]
    public async Task ReleaseRemindAsync_AlreadyMonitored_ReportsError()
    {
        var repository = new Mock<IReleaseDateReminderRepository>();
        repository.Setup(r => r.GetForUserAsync(42ul, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ReleaseDateReminderItem>
            {
                new() { DiscordUserId = 42ul, AppId = 10u, Name = "Game" }
            });
        var steamStore = new Mock<ISteamStoreService>();
        steamStore.Setup(s => s.SearchGameAsync("Game", It.IsAny<CancellationToken>()))
            .ReturnsAsync((10u, "Game"));

        var module = CreateModule(repository, steamStore);

        await module.ReleaseRemindAsync("Game");

        Assert.Contains("wird bereits", string.Join(" ", module.Followups));
    }

    [Fact]
    public async Task ReleaseRemindAsync_Success_AddsReminder()
    {
        var repository = new Mock<IReleaseDateReminderRepository>();
        repository.Setup(r => r.GetForUserAsync(42ul, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ReleaseDateReminderItem>());
        var steamStore = new Mock<ISteamStoreService>();
        steamStore.Setup(s => s.SearchGameAsync("Game", It.IsAny<CancellationToken>()))
            .ReturnsAsync((10u, "Game"));
        steamStore.Setup(s => s.GetAppDetailsAsync(10u, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SteamStoreAppDetails(10u, "Game", DateTime.UtcNow.AddDays(5), false, false));

        var module = CreateModule(repository, steamStore);

        await module.ReleaseRemindAsync("Game");

        repository.Verify(r => r.AddAsync(It.IsAny<ReleaseDateReminderItem>(), It.IsAny<CancellationToken>()), Times.Once);
        Assert.Contains("wird überwacht", string.Join(" ", module.Followups));
    }

    [Fact]
    public async Task ReleaseUnremindAsync_Removed_ReportsSuccess()
    {
        var repository = new Mock<IReleaseDateReminderRepository>();
        repository.Setup(r => r.RemoveAsync(42ul, 7u, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var steamStore = new Mock<ISteamStoreService>();

        var module = CreateModule(repository, steamStore);

        await module.ReleaseUnremindAsync(7);

        Assert.Contains("wurde aus den Release-Erinnerungen entfernt", string.Join(" ", module.Followups));
    }

    [Fact]
    public async Task ReleaseUnremindAsync_NotRemoved_ReportsFailure()
    {
        var repository = new Mock<IReleaseDateReminderRepository>();
        repository.Setup(r => r.RemoveAsync(42ul, 7u, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        var steamStore = new Mock<ISteamStoreService>();

        var module = CreateModule(repository, steamStore);

        await module.ReleaseUnremindAsync(7);

        Assert.Contains("wird nicht überwacht", string.Join(" ", module.Followups));
    }

    [Fact]
    public async Task ReleaseListAsync_Empty_ReportsEmpty()
    {
        var repository = new Mock<IReleaseDateReminderRepository>();
        repository.Setup(r => r.GetForUserAsync(42ul, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ReleaseDateReminderItem>());
        var steamStore = new Mock<ISteamStoreService>();

        var module = CreateModule(repository, steamStore);

        await module.ReleaseListAsync();

        Assert.Contains("überwachst keine Releases", string.Join(" ", module.Followups));
    }

    [Fact]
    public void Commands_AreSlashCommands()
    {
        Assert.NotNull(typeof(ReleaseDateReminderModule).GetMethod(nameof(ReleaseDateReminderModule.ReleaseRemindAsync))!
            .GetCustomAttribute<SlashCommandAttribute>());
    }
}
