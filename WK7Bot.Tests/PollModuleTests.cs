using System.Reflection;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.Extensions.Logging.Abstractions;
using WK7Bot.Core.Utilities;
using WK7Bot.Modules;
using Xunit;

namespace WK7Bot.Tests;

public class PollModuleTests
{
    private sealed class TestPollModule : PollModule
    {
        public int DeferCount { get; private set; }

        public List<PollProperties> Polls { get; } = new();

        public List<string> Followups { get; } = new();

        public TestPollModule()
            : base(NullLogger<PollModule>.Instance)
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
            if (poll != null)
            {
                Polls.Add(poll);
            }
            else
            {
                Followups.Add(text ?? string.Empty);
            }

            return Task.FromResult<IUserMessage>(null!);
        }
    }

    private static TestPollModule CreateModule(string username = "Tester", ulong userId = 42)
    {
        var module = new TestPollModule();
        var context = DiscordTestDoubles.CreateContext();
        DiscordTestDoubles.SetUser(context, username, userId);
        ((IInteractionModuleBase)module).SetContext(context);
        return module;
    }

    [Fact]
    public void Constructor_Throws_WhenLoggerIsNull()
    {
        Assert.Throws<ArgumentNullException>(() => new PollModule(null!));
    }

    [Fact]
    public void PollCommand_IsExposedAsSlashCommand()
    {
        var method = typeof(PollModule).GetMethod(nameof(PollModule.PollAsync))!;
        var attribute = method.GetCustomAttribute<SlashCommandAttribute>();

        Assert.NotNull(attribute);
        Assert.Equal("poll", attribute!.Name);
        Assert.False(string.IsNullOrWhiteSpace(attribute.Description));

        var parameters = method.GetParameters();
        Assert.Equal("frage", parameters[0].GetCustomAttribute<SummaryAttribute>()?.Name);
        Assert.Equal("optionen", parameters[1].GetCustomAttribute<SummaryAttribute>()?.Name);
    }

    [Fact]
    public async Task PollAsync_ReturnsValidationError_WhenQuestionIsEmpty()
    {
        var module = CreateModule();

        await module.PollAsync("   ", "A|B");

        Assert.Equal(1, module.DeferCount);
        Assert.Empty(module.Polls);
        var followup = Assert.Single(module.Followups);
        Assert.StartsWith("❌", followup);
        Assert.Contains("darf nicht leer sein", followup);
    }

    [Fact]
    public async Task PollAsync_ReturnsValidationError_WhenTooFewOptions()
    {
        var module = CreateModule();

        await module.PollAsync("Frage?", "nur eine");

        Assert.Equal(1, module.DeferCount);
        Assert.Empty(module.Polls);
        var followup = Assert.Single(module.Followups);
        Assert.StartsWith("❌", followup);
        Assert.Contains("mindestens 2", followup);
    }

    [Fact]
    public async Task PollAsync_PostsNativePoll_WhenValid()
    {
        var module = CreateModule();

        await module.PollAsync("Frage?", "A|B|C");

        Assert.Equal(1, module.DeferCount);
        Assert.Single(module.Polls);
        var poll = Assert.Single(module.Polls);
        Assert.Equal("Frage?", poll.Question.Text);
        Assert.Equal(3, poll.Answers.Count);
        Assert.Equal("A", poll.Answers[0].Text);
        Assert.Equal("B", poll.Answers[1].Text);
        Assert.Equal("C", poll.Answers[2].Text);
        Assert.Equal(24u, poll.Duration);
        Assert.False(poll.AllowMultiselect);
        Assert.Equal(PollLayout.Default, poll.LayoutType);
    }

    [Fact]
    public async Task PollAsync_ClampsDurationToMaximum()
    {
        var module = CreateModule();

        await module.PollAsync("Frage?", "A|B", dauer_stunden: 999);

        var poll = Assert.Single(module.Polls);
        Assert.Equal((uint)PollOptionParser.MaxDurationHours, poll.Duration);
    }

    [Fact]
    public async Task PollAsync_RespectsMultiselect()
    {
        var module = CreateModule();

        await module.PollAsync("Frage?", "A|B", mehrfachauswahl: true);

        var poll = Assert.Single(module.Polls);
        Assert.True(poll.AllowMultiselect);
    }

    [Fact]
    public async Task PollAsync_TruncatesLongOptions()
    {
        var module = CreateModule();
        var longOption = new string('x', PollOptionParser.MaxOptionLength + 10);

        await module.PollAsync("Frage?", $"A|{longOption}");

        var poll = Assert.Single(module.Polls);
        Assert.Equal(PollOptionParser.MaxOptionLength, poll.Answers[1].Text.Length);
    }
}
