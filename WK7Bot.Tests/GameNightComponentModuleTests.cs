using System.Reflection;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using WK7Bot.Core.Entities;
using WK7Bot.Core.Interfaces;
using WK7Bot.Modules;
using Xunit;

namespace WK7Bot.Tests;

public class GameNightComponentModuleTests
{
    private sealed class TestGameNightComponentModule : GameNightComponentModule
    {
        public int DeferCount { get; private set; }
        public List<string> Followups { get; } = new();
        public List<(GameNight GameNight, Embed Embed, MessageComponent Components)> Edits { get; } = new();
        public bool ThrowOnEdit { get; set; }

        public TestGameNightComponentModule(IGameNightRepository repository, ILogger<GameNightComponentModule> logger)
            : base(repository, logger)
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

        protected override Task EditGameNightMessageAsync(GameNight gameNight, Embed embed, MessageComponent components)
        {
            if (ThrowOnEdit) throw new InvalidOperationException("edit failed");
            Edits.Add((gameNight, embed, components));
            return Task.CompletedTask;
        }
    }

    private static TestGameNightComponentModule CreateModule(Mock<IGameNightRepository>? repository = null, ulong userId = 100)
    {
        var module = new TestGameNightComponentModule(
            repository?.Object ?? Mock.Of<IGameNightRepository>(),
            NullLogger<GameNightComponentModule>.Instance);
        var context = DiscordTestDoubles.CreateContext();
        DiscordTestDoubles.SetUser(context, "Tester", userId);
        ((IInteractionModuleBase)module).SetContext(context);
        return module;
    }

    [Fact]
    public void Constructor_Throws_WhenDependenciesAreNull()
    {
        Assert.Throws<ArgumentNullException>(() => new GameNightComponentModule(null!, NullLogger<GameNightComponentModule>.Instance));
        Assert.Throws<ArgumentNullException>(() => new GameNightComponentModule(Mock.Of<IGameNightRepository>(), null!));
    }

    [Fact]
    public void Handler_IsComponentInteraction()
    {
        var attribute = typeof(GameNightComponentModule).GetMethod(nameof(GameNightComponentModule.HandleVoteAsync))!
            .GetCustomAttribute<ComponentInteractionAttribute>();

        Assert.NotNull(attribute);
        Assert.Equal("game-night:*", attribute!.CustomId);
    }

    [Fact]
    public async Task HandleVoteAsync_ReportsInvalidPayload()
    {
        var module = CreateModule();

        await module.HandleVoteAsync("invalid");

        Assert.Equal(1, module.DeferCount);
        Assert.Single(module.Followups);
        Assert.Contains("nicht mehr verfügbar", module.Followups[0]);
    }

    [Fact]
    public async Task HandleVoteAsync_ReportsSlotNotFound()
    {
        var repository = new Mock<IGameNightRepository>();
        repository.Setup(r => r.GetSlotAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync((GameNightSlot?)null);
        var module = CreateModule(repository);

        await module.HandleVoteAsync("toggle:1");

        Assert.Equal(1, module.DeferCount);
        Assert.Single(module.Followups);
        Assert.Contains("nicht mehr verfügbar", module.Followups[0]);
    }

    [Fact]
    public async Task HandleVoteAsync_ReportsGameNightNotFound()
    {
        var repository = new Mock<IGameNightRepository>();
        repository.Setup(r => r.GetSlotAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GameNightSlot { Id = 1, GameNightId = 1 });
        repository.Setup(r => r.GetAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync((GameNight?)null);
        var module = CreateModule(repository);

        await module.HandleVoteAsync("toggle:1");

        Assert.Equal(1, module.DeferCount);
        Assert.Single(module.Followups);
        Assert.Contains("nicht mehr verfügbar", module.Followups[0]);
    }

    [Fact]
    public async Task HandleVoteAsync_ReportsInactiveGameNight()
    {
        var repository = new Mock<IGameNightRepository>();
        repository.Setup(r => r.GetSlotAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GameNightSlot { Id = 1, GameNightId = 1 });
        repository.Setup(r => r.GetAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GameNight { Id = 1, Closed = true, ClosesAt = DateTime.UtcNow.AddHours(-1) });
        var module = CreateModule(repository);

        await module.HandleVoteAsync("toggle:1");

        Assert.Equal(1, module.DeferCount);
        Assert.Single(module.Followups);
        Assert.Contains("bereits beendet", module.Followups[0]);
    }

    [Fact]
    public async Task HandleVoteAsync_SetsVote_WhenNotVoted()
    {
        var repository = new Mock<IGameNightRepository>();
        repository.Setup(r => r.GetSlotAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GameNightSlot { Id = 1, GameNightId = 1, Label = "Fri 20:00" });
        repository.Setup(r => r.GetAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GameNight { Id = 1, ClosesAt = DateTime.UtcNow.AddHours(1) });
        repository.Setup(r => r.GetVoteAsync(1, 1, 100, It.IsAny<CancellationToken>())).ReturnsAsync((GameNightVote?)null);
        repository.Setup(r => r.SetVoteAsync(It.IsAny<GameNightVote>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        repository.Setup(r => r.GetSlotsAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<GameNightSlot> { new() { Id = 1, GameNightId = 1, Label = "Fri 20:00" } });
        repository.Setup(r => r.GetVotesAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(new List<GameNightVote>());
        var module = CreateModule(repository, userId: 100);

        await module.HandleVoteAsync("toggle:1");

        Assert.Equal(1, module.DeferCount);
        repository.Verify(r => r.SetVoteAsync(It.Is<GameNightVote>(v => v.UserId == 100 && v.SlotId == 1 && v.Available), It.IsAny<CancellationToken>()), Times.Once);
        Assert.Single(module.Followups);
        Assert.Contains("verfügbar", module.Followups[0]);
        Assert.Single(module.Edits);
    }

    [Fact]
    public async Task HandleVoteAsync_RemovesVote_WhenAlreadyVoted()
    {
        var repository = new Mock<IGameNightRepository>();
        repository.Setup(r => r.GetSlotAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GameNightSlot { Id = 1, GameNightId = 1, Label = "Fri 20:00" });
        repository.Setup(r => r.GetAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GameNight { Id = 1, ClosesAt = DateTime.UtcNow.AddHours(1) });
        repository.Setup(r => r.GetVoteAsync(1, 1, 100, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GameNightVote { GameNightId = 1, SlotId = 1, UserId = 100, Available = true });
        repository.Setup(r => r.RemoveVoteAsync(1, 1, 100, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        repository.Setup(r => r.GetSlotsAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<GameNightSlot> { new() { Id = 1, GameNightId = 1, Label = "Fri 20:00" } });
        repository.Setup(r => r.GetVotesAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(new List<GameNightVote>());
        var module = CreateModule(repository, userId: 100);

        await module.HandleVoteAsync("toggle:1");

        Assert.Equal(1, module.DeferCount);
        repository.Verify(r => r.RemoveVoteAsync(1, 1, 100, It.IsAny<CancellationToken>()), Times.Once);
        repository.Verify(r => r.SetVoteAsync(It.IsAny<GameNightVote>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Single(module.Followups);
        Assert.Contains("nicht mehr verfügbar", module.Followups[0]);
        Assert.Single(module.Edits);
    }

    [Fact]
    public async Task HandleVoteAsync_Continues_WhenEditFails()
    {
        var repository = new Mock<IGameNightRepository>();
        repository.Setup(r => r.GetSlotAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GameNightSlot { Id = 1, GameNightId = 1, Label = "Fri 20:00" });
        repository.Setup(r => r.GetAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GameNight { Id = 1, ClosesAt = DateTime.UtcNow.AddHours(1) });
        repository.Setup(r => r.GetVoteAsync(1, 1, 100, It.IsAny<CancellationToken>())).ReturnsAsync((GameNightVote?)null);
        repository.Setup(r => r.SetVoteAsync(It.IsAny<GameNightVote>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        repository.Setup(r => r.GetSlotsAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<GameNightSlot> { new() { Id = 1, GameNightId = 1, Label = "Fri 20:00" } });
        repository.Setup(r => r.GetVotesAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(new List<GameNightVote>());
        var module = CreateModule(repository, userId: 100);
        module.ThrowOnEdit = true;

        await module.HandleVoteAsync("toggle:1");

        Assert.Single(module.Followups);
        Assert.Contains("verfügbar", module.Followups[0]);
    }

    [Fact]
    public async Task HandleVoteAsync_ReportsError_WhenRepositoryThrows()
    {
        var repository = new Mock<IGameNightRepository>();
        repository.Setup(r => r.GetSlotAsync(1, It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("db down"));
        var module = CreateModule(repository);

        await module.HandleVoteAsync("toggle:1");

        Assert.Equal(1, module.DeferCount);
        Assert.Single(module.Followups);
        Assert.Contains("konnte nicht gespeichert werden", module.Followups[0]);
    }
}
