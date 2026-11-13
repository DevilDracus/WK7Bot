using System.Reflection;
using System.Runtime.CompilerServices;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.Extensions.Logging.Abstractions;
using Options = Microsoft.Extensions.Options.Options;
using Moq;
using WK7Bot.Core.Entities;
using WK7Bot.Core.Interfaces;
using WK7Bot.Modules;
using Xunit;

namespace WK7Bot.Tests;

public class XpModuleTests
{
    private sealed class TestModule : XpModule
    {
        public int DeferCount { get; private set; }
        public List<string> Followups { get; } = new();

        public TestModule(IXpRepository repository)
            : base(repository, Microsoft.Extensions.Options.Options.Create(new Wk7BotOptions()), NullLogger<XpModule>.Instance) { }

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

    private static TestModule CreateModule(Mock<IXpRepository> repository, ulong userId = 42)
    {
        var module = new TestModule(repository.Object);
        var context = DiscordTestDoubles.CreateContext();
        DiscordTestDoubles.SetUser(context, "tester", userId);
        ((IInteractionModuleBase)module).SetContext(context);
        return module;
    }

    private static IUser CreateUser(ulong id)
    {
        var user = new Mock<IUser>();
        user.SetupGet(u => u.Id).Returns(id);
        return user.Object;
    }

    [Fact]
    public async Task RankAsync_NoProfile_ReportsZero()
    {
        var repository = new Mock<IXpRepository>();
        repository.Setup(r => r.GetByUserAsync(42ul, 0, It.IsAny<CancellationToken>()))
            .ReturnsAsync((XpProfile?)null);
        var module = CreateModule(repository);

        await module.RankAsync();

        Assert.Equal(1, module.DeferCount);
        Assert.Contains("Level 0", string.Join(" ", module.Followups));
    }

    [Fact]
    public async Task LeaderboardAsync_Empty_ReportsEmpty()
    {
        var repository = new Mock<IXpRepository>();
        repository.Setup(r => r.GetLeaderboardAsync(0, 10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<XpProfile>());
        var module = CreateModule(repository);

        await module.LeaderboardAsync();

        Assert.Contains("Noch keine XP-Einträge", string.Join(" ", module.Followups));
    }

    [Fact]
    public async Task ResetAsync_Success_ReportsSuccess()
    {
        var repository = new Mock<IXpRepository>();
        var module = CreateModule(repository);

        await module.ResetAsync(CreateUser(7));

        repository.Verify(r => r.ResetAsync(7ul, 0, It.IsAny<CancellationToken>()), Times.Once);
        Assert.Contains("zurückgesetzt", string.Join(" ", module.Followups));
    }

    [Fact]
    public async Task SetAsync_Success_ReportsSuccess()
    {
        var repository = new Mock<IXpRepository>();
        var module = CreateModule(repository);

        await module.SetAsync(CreateUser(7), 12);

        repository.Verify(r => r.SetLevelAsync(7ul, 0, 12, It.IsAny<CancellationToken>()), Times.Once);
        Assert.Contains("Level 12", string.Join(" ", module.Followups));
    }

    [Fact]
    public void AdminCommands_RequireAdministrator()
    {
        var reset = typeof(XpModule).GetMethod(nameof(XpModule.ResetAsync))!
            .GetCustomAttribute<RequireUserPermissionAttribute>();
        var set = typeof(XpModule).GetMethod(nameof(XpModule.SetAsync))!
            .GetCustomAttribute<RequireUserPermissionAttribute>();

        Assert.NotNull(reset);
        Assert.NotNull(set);
        Assert.True((reset!.GuildPermission & GuildPermission.Administrator) == GuildPermission.Administrator);
    }
}
