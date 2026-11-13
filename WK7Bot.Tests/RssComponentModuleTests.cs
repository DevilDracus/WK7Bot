using System.Reflection;
using System.Runtime.CompilerServices;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using WK7Bot.Core.Interfaces;
using WK7Bot.Modules;
using Xunit;

namespace WK7Bot.Tests;

public class RssComponentModuleTests
{
    private sealed class TestModule : RssComponentModule
    {
        public int DeferCount { get; private set; }
        public List<string> Followups { get; } = new();

        public TestModule(IRssRepository repository)
            : base(repository, NullLogger<RssComponentModule>.Instance) { }

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

    private static TestModule CreateModule(Mock<IRssRepository> repository)
    {
        var module = new TestModule(repository.Object);
        var context = DiscordTestDoubles.CreateContext();
        DiscordTestDoubles.SetUser(context, "tester");
        ((IInteractionModuleBase)module).SetContext(context);
        return module;
    }

    [Fact]
    public async Task HandleSubscriptionSelectionAsync_NonGuildUser_ReportsError()
    {
        var repository = new Mock<IRssRepository>();
        var module = CreateModule(repository);

        await module.HandleSubscriptionSelectionAsync(Array.Empty<string>());

        Assert.Equal(1, module.DeferCount);
        Assert.Contains("innerhalb eines Servers", string.Join(" ", module.Followups));
    }

    [Fact]
    public void Handler_IsComponentInteraction()
    {
        var attribute = typeof(RssComponentModule).GetMethod(nameof(RssComponentModule.HandleSubscriptionSelectionAsync))!
            .GetCustomAttribute<ComponentInteractionAttribute>();

        Assert.NotNull(attribute);
        Assert.Equal("rss-subscription-select", attribute!.CustomId);
    }
}
