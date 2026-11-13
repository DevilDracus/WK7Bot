using System.Reflection;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using WK7Bot.Modules;
using WK7Bot.Services.Interfaces;
using Xunit;

namespace WK7Bot.Tests;

public class LeaderboardModuleTests
{
    private sealed class TestLeaderboardModule : LeaderboardModule
    {
        public int DeferCount { get; private set; }

        public List<string> Followups { get; } = new();

        public List<(MessageComponent Components, MessageFlags Flags)> ComponentPosts { get; } = new();

        public TestLeaderboardModule(ILeaderboardService leaderboardService)
            : base(leaderboardService, NullLogger<LeaderboardModule>.Instance)
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

            if (components != null)
            {
                ComponentPosts.Add((components, flags));
            }

            return Task.FromResult<IUserMessage>(null!);
        }
    }

    private static TestLeaderboardModule CreateModule(
        ILeaderboardService service,
        ulong? guildId = 1,
        string username = "Tester",
        ulong userId = 42)
    {
        var module = new TestLeaderboardModule(service);
        var context = DiscordTestDoubles.CreateContext();
        DiscordTestDoubles.SetUser(context, username, userId);
        if (guildId.HasValue)
        {
            DiscordTestDoubles.SetGuild(context, guildId.Value);
        }

        ((IInteractionModuleBase)module).SetContext(context);
        return module;
    }

    private static LeaderboardResult CreateResult(params LeaderboardEntry[] entries)
        => new("🏆 Test Leaderboard", entries.ToList());

    [Fact]
    public void Constructor_Throws_WhenDependenciesAreNull()
    {
        Assert.Throws<ArgumentNullException>(() => new LeaderboardModule(null!, NullLogger<LeaderboardModule>.Instance));
        Assert.Throws<ArgumentNullException>(() => new LeaderboardModule(new Mock<ILeaderboardService>().Object, null!));
    }

    [Fact]
    public void LeaderboardCommand_IsExposedAsSlashCommand()
    {
        var method = typeof(LeaderboardModule).GetMethod(nameof(LeaderboardModule.LeaderboardAsync))!;
        var attribute = method.GetCustomAttribute<SlashCommandAttribute>();

        Assert.NotNull(attribute);
        Assert.Equal("leaderboard", attribute!.Name);
        Assert.Contains("Bestenliste", attribute.Description);

        var typeParameter = method.GetParameters()[0];
        Assert.Equal("type", typeParameter.GetCustomAttribute<SummaryAttribute>()?.Name);
        Assert.True(typeParameter.HasDefaultValue);

        var limitParameter = method.GetParameters()[1];
        Assert.Equal("limit", limitParameter.GetCustomAttribute<SummaryAttribute>()?.Name);
        Assert.True(limitParameter.HasDefaultValue);
    }

    [Fact]
    public async Task LeaderboardAsync_ReportsMissingGuild_WhenCalledInDM()
    {
        var service = new Mock<ILeaderboardService>();
        var module = CreateModule(service.Object, guildId: null);

        await module.LeaderboardAsync();

        Assert.Equal(1, module.DeferCount);
        var followup = Assert.Single(module.Followups);
        Assert.Contains("nur in einem Server", followup);
        service.Verify(s => s.GetAsync(It.IsAny<string>(), It.IsAny<ulong>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task LeaderboardAsync_ReportsNoLinkedPlayers_WhenEntriesAreEmpty()
    {
        var service = new Mock<ILeaderboardService>();
        service.Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<ulong>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateResult());
        var module = CreateModule(service.Object);

        await module.LeaderboardAsync();

        Assert.Equal(1, module.DeferCount);
        var followup = Assert.Single(module.Followups);
        Assert.Contains("Keine verknüpften Spieler", followup);
        Assert.Empty(module.ComponentPosts);
    }

    [Fact]
    public async Task LeaderboardAsync_PostsComponentsV2_WhenEntriesExist()
    {
        var service = new Mock<ILeaderboardService>();
        service.Setup(s => s.GetAsync(It.IsAny<string>(), 1ul, 10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateResult(new LeaderboardEntry(1, "Alice", "42 h", "detail")));
        var module = CreateModule(service.Object);

        await module.LeaderboardAsync();

        Assert.Equal(1, module.DeferCount);
        Assert.Empty(module.Followups);
        var post = Assert.Single(module.ComponentPosts);
        Assert.Equal(MessageFlags.ComponentsV2, post.Flags);
        Assert.NotNull(post.Components);
    }

    [Fact]
    public async Task LeaderboardAsync_RespectsLimitParameter()
    {
        var service = new Mock<ILeaderboardService>();
        service.Setup(s => s.GetAsync(It.IsAny<string>(), 1ul, 5, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateResult(new LeaderboardEntry(1, "Alice", "42 h")));
        var module = CreateModule(service.Object);

        await module.LeaderboardAsync(limit: 5);

        service.Verify(s => s.GetAsync("steam-playtime", 1ul, 5, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task LeaderboardAsync_ReportsError_WhenServiceThrows()
    {
        var service = new Mock<ILeaderboardService>();
        service.Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<ulong>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("service down"));
        var module = CreateModule(service.Object);

        await module.LeaderboardAsync();

        Assert.Equal(1, module.DeferCount);
        var followup = Assert.Single(module.Followups);
        Assert.Contains("konnte nicht geladen werden", followup);
    }
}
