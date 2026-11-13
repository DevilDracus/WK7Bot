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
using Xunit;

namespace WK7Bot.Tests;

public class RemindModuleTests
{
    private sealed class TestModule : RemindModule
    {
        public int DeferCount { get; private set; }
        public List<string> Followups { get; } = new();

        public TestModule(IReminderRepository repository)
            : base(repository, NullLogger<RemindModule>.Instance) { }

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

    private static TestModule CreateModule(Mock<IReminderRepository> repository, ulong userId = 42)
    {
        var module = new TestModule(repository.Object);
        var context = DiscordTestDoubles.CreateContext();
        DiscordTestDoubles.SetUser(context, "tester", userId);
        ((IInteractionModuleBase)module).SetContext(context);
        return module;
    }

    [Fact]
    public async Task RemindAsync_InvalidTime_ReportsError()
    {
        var repository = new Mock<IReminderRepository>();
        var module = CreateModule(repository);

        await module.RemindAsync("not-a-time", "test");

        Assert.Equal(1, module.DeferCount);
        Assert.Contains("❌", module.Followups[0]);
    }

    [Fact]
    public async Task RemindAsync_EmptyText_ReportsError()
    {
        var repository = new Mock<IReminderRepository>();
        var module = CreateModule(repository);

        await module.RemindAsync("in 30m", "   ");

        Assert.Contains("Bitte gib an", string.Join(" ", module.Followups));
    }

    [Fact]
    public async Task RemindAsync_Valid_StoresReminder()
    {
        var repository = new Mock<IReminderRepository>();
        var module = CreateModule(repository);

        await module.RemindAsync("in 30m", "take a break");

        repository.Verify(r => r.AddAsync(It.IsAny<Reminder>(), It.IsAny<CancellationToken>()), Times.Once);
        Assert.Contains("gesetzt", string.Join(" ", module.Followups));
    }

    [Fact]
    public async Task RemindAsync_RepositoryThrows_ReportsError()
    {
        var repository = new Mock<IReminderRepository>();
        repository.Setup(r => r.AddAsync(It.IsAny<Reminder>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("db"));
        var module = CreateModule(repository);

        await module.RemindAsync("in 30m", "fail");

        Assert.Contains("konnte nicht gespeichert", string.Join(" ", module.Followups));
    }

    [Fact]
    public async Task ListAsync_NoReminders_ReportsEmpty()
    {
        var repository = new Mock<IReminderRepository>();
        repository.Setup(r => r.GetPendingForUserAsync(42ul, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Reminder>());
        var module = CreateModule(repository);

        await module.ListAsync();

        Assert.Contains("keine offenen Erinnerungen", string.Join(" ", module.Followups));
    }

    [Fact]
    public async Task CancelAsync_NotFound_ReportsFailure()
    {
        var repository = new Mock<IReminderRepository>();
        repository.Setup(r => r.RemovePendingAsync(5, 42ul, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        var module = CreateModule(repository);

        await module.CancelAsync(5);

        Assert.Contains("Keine offene", string.Join(" ", module.Followups));
    }

    [Fact]
    public void Commands_AreSlashCommands()
    {
        Assert.NotNull(typeof(RemindModule).GetMethod(nameof(RemindModule.RemindAsync))!
            .GetCustomAttribute<SlashCommandAttribute>());
    }
}
