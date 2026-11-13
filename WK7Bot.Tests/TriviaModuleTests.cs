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

public class TriviaModuleTests
{
    private sealed class TestModule : TriviaModule
    {
        public int DeferCount { get; private set; }
        public List<string> Followups { get; } = new();
        public ulong ReturnedMessageId { get; } = 424242;

        public TestModule(ITriviaScoreRepository repository)
            : base(repository, NullLogger<TriviaModule>.Instance) { }

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

            var message = new Mock<IUserMessage>();
            message.SetupGet(m => m.Id).Returns(ReturnedMessageId);
            return Task.FromResult(message.Object);
        }
    }

    private static TestModule CreateModule(Mock<ITriviaScoreRepository> repository, ulong userId = 42)
    {
        var module = new TestModule(repository.Object);
        var context = DiscordTestDoubles.CreateContext();
        DiscordTestDoubles.SetUser(context, "tester", userId);
        ((IInteractionModuleBase)module).SetContext(context);
        return module;
    }

    [Fact]
    public async Task QuizAsync_DeferredAndPostsQuestion()
    {
        var repository = new Mock<ITriviaScoreRepository>();
        var module = CreateModule(repository);

        await module.QuizAsync();

        Assert.Equal(1, module.DeferCount);
        Assert.Single(module.Followups);
    }

    [Fact]
    public async Task AnswerAsync_NoActiveRound_ReportsInactive()
    {
        var repository = new Mock<ITriviaScoreRepository>();
        var module = CreateModule(repository);

        await module.AnswerAsync(0);

        Assert.Equal(1, module.DeferCount);
        Assert.Contains("nicht mehr aktiv", string.Join(" ", module.Followups));
    }

    [Fact]
    public async Task LeaderboardAsync_Empty_ReportsEmpty()
    {
        var repository = new Mock<ITriviaScoreRepository>();
        repository.Setup(r => r.GetTopAsync(0, It.IsAny<DateTime>(), 10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Core.Entities.TriviaScore>());
        var module = CreateModule(repository);

        await module.LeaderboardAsync();

        Assert.Contains("Noch keine Punkte", string.Join(" ", module.Followups));
    }

    [Fact]
    public void Commands_AreRegistered()
    {
        var quiz = typeof(TriviaModule).GetMethod(nameof(TriviaModule.QuizAsync))!
            .GetCustomAttribute<SlashCommandAttribute>();
        var leaderboard = typeof(TriviaModule).GetMethod(nameof(TriviaModule.LeaderboardAsync))!
            .GetCustomAttribute<SlashCommandAttribute>();

        Assert.NotNull(quiz);
        Assert.NotNull(leaderboard);
    }
}
