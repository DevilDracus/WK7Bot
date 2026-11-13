using System.Reflection;
using System.Runtime.CompilerServices;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using WK7Bot.Core.Interfaces;
using WK7Bot.Modules;
using WK7Bot.Services;
using WK7Bot.Services.Interfaces;
using Xunit;

namespace WK7Bot.Tests;

public class RssModuleTests
{
    private sealed class TestModule : RssModule
    {
        public int DeferCount { get; private set; }
        public List<string> Followups { get; } = new();

        public TestModule(IRssRepository repository, RssParserService parser, IOpmlService opmlService)
            : base(repository, parser, opmlService, NullLogger<RssModule>.Instance) { }

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

    private static TestModule CreateModule(Mock<IRssRepository>? repository = null, IOpmlService? opmlService = null)
    {
        var repo = repository ?? new Mock<IRssRepository>();
        var parser = new RssParserService(NullLogger<RssParserService>.Instance);
        var module = new TestModule(repo.Object, parser, opmlService ?? new Mock<IOpmlService>().Object);
        var context = DiscordTestDoubles.CreateContext();
        DiscordTestDoubles.SetUser(context, "tester");
        ((IInteractionModuleBase)module).SetContext(context);
        return module;
    }

    [Fact]
    public async Task TestFeedAsync_InvalidUrl_ReportsError()
    {
        var module = CreateModule();

        await module.TestFeedAsync("not-a-url");

        Assert.Equal(1, module.DeferCount);
        Assert.Contains("❌ Bitte gib", string.Join(" ", module.Followups));
    }

    [Fact]
    public async Task ListFeedsAsync_Empty_ReportsEmpty()
    {
        var repository = new Mock<IRssRepository>();
        repository.Setup(r => r.GetAllFeedsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Core.Entities.RssFeed>());
        var module = CreateModule(repository);

        await module.ListFeedsAsync();

        Assert.Contains("Noch keine RSS-Feeds", string.Join(" ", module.Followups));
    }

    [Fact]
    public async Task ListFeedsAsync_RepositoryThrows_ReportsError()
    {
        var repository = new Mock<IRssRepository>();
        repository.Setup(r => r.GetAllFeedsAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("db"));
        var module = CreateModule(repository);

        await module.ListFeedsAsync();

        Assert.Contains("konnten nicht geladen", string.Join(" ", module.Followups));
    }

    [Theory]
    [InlineData("   ")]
    [InlineData("")]
    public async Task RemoveFeedAsync_BlankName_ReportsError(string name)
    {
        var module = CreateModule();

        await module.RemoveFeedAsync(name);

        Assert.Contains("Bitte gib den Namen", string.Join(" ", module.Followups));
    }

    [Fact]
    public async Task RemoveFeedAsync_NotFound_ReportsError()
    {
        var repository = new Mock<IRssRepository>();
        repository.Setup(r => r.GetFeedByNameAsync("missing", It.IsAny<CancellationToken>()))
            .ReturnsAsync((Core.Entities.RssFeed?)null);
        var module = CreateModule(repository);

        await module.RemoveFeedAsync("missing");

        Assert.Contains("Kein Feed", string.Join(" ", module.Followups));
    }

    [Fact]
    public void ManagementCommands_RequireManageChannels()
    {
        var add = typeof(RssModule).GetMethod(nameof(RssModule.AddFeedAsync))!
            .GetCustomAttribute<RequireUserPermissionAttribute>();
        var remove = typeof(RssModule).GetMethod(nameof(RssModule.RemoveFeedAsync))!
            .GetCustomAttribute<RequireUserPermissionAttribute>();

        Assert.NotNull(add);
        Assert.NotNull(remove);
        Assert.True((add!.GuildPermission & GuildPermission.ManageChannels) == GuildPermission.ManageChannels);
    }
}
