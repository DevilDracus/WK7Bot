using System.Reflection;
using System.Runtime.CompilerServices;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using WK7Bot.Core.Entities;
using WK7Bot.Core.Interfaces;
using WK7Bot.Modules;
using WK7Bot.Options;
using WK7Bot.Services.Interfaces;
using Xunit;

namespace WK7Bot.Tests;

public class GameNightModuleTests
{
    private sealed class TestGameNightModule : GameNightModule
    {
        public int DeferCount { get; private set; }
        public List<string> Followups { get; } = new();
        public ITextChannel? ReturnedChannel { get; set; }
        public IUserMessage? PostedMessage { get; set; }
        public List<(ITextChannel Channel, string Text, Embed Embed, MessageComponent Components)> Posts { get; } = new();
        public bool ThrowOnPost { get; set; }

        public TestGameNightModule(
            IGameNightRepository repository,
            IDiscordScheduledEventService scheduledEventService,
            IReminderRepository? reminderRepository,
            IOptions<ServersOptions>? serversOptions,
            ILogger<GameNightModule> logger)
            : base(repository, scheduledEventService, reminderRepository, serversOptions, logger)
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

        protected override Task<ITextChannel?> GetTreffChannelAsync()
            => Task.FromResult(ReturnedChannel);

        protected override Task<IUserMessage> PostToTreffChannelAsync(ITextChannel channel, string text, Embed embed, MessageComponent components)
        {
            if (ThrowOnPost) throw new InvalidOperationException("post failed");
            Posts.Add((channel, text, embed, components));
            return Task.FromResult(PostedMessage ?? Mock.Of<IUserMessage>(m => m.Id == 12345UL));
        }
    }

    private static TestGameNightModule CreateModule(
        Mock<IGameNightRepository>? repository = null,
        Mock<IDiscordScheduledEventService>? scheduledEventService = null,
        Mock<IReminderRepository>? reminderRepository = null,
        IOptions<ServersOptions>? serversOptions = null,
        ulong guildId = 1,
        ulong userId = 100)
    {
        var module = new TestGameNightModule(
            repository?.Object ?? Mock.Of<IGameNightRepository>(),
            scheduledEventService?.Object ?? Mock.Of<IDiscordScheduledEventService>(),
            reminderRepository?.Object,
            serversOptions,
            NullLogger<GameNightModule>.Instance)
        {
            ReturnedChannel = Mock.Of<ITextChannel>(c => c.Id == 98765UL)
        };

        var context = DiscordTestDoubles.CreateContext();
        DiscordTestDoubles.SetGuild(context, guildId);
        DiscordTestDoubles.SetUser(context, "Tester", userId);
        DiscordTestDoubles.SetPropertyOrField(context, typeof(SocketInteractionContext), "Channel", CreateSocketTextChannel(55555UL));
        ((IInteractionModuleBase)module).SetContext(context);
        return module;
    }

    private static SocketTextChannel CreateSocketTextChannel(ulong id)
    {
        var channel = (SocketTextChannel)RuntimeHelpers.GetUninitializedObject(typeof(SocketTextChannel));
        DiscordTestDoubles.SetPropertyOrField(channel, typeof(SocketTextChannel), "Id", id);
        return channel;
    }

    [Fact]
    public void Constructor_Throws_WhenDependenciesAreNull()
    {
        var repository = Mock.Of<IGameNightRepository>();
        var scheduledEventService = Mock.Of<IDiscordScheduledEventService>();
        var logger = NullLogger<GameNightModule>.Instance;

        Assert.Throws<ArgumentNullException>(() => new GameNightModule(null!, scheduledEventService, null, null, logger));
        Assert.Throws<ArgumentNullException>(() => new GameNightModule(repository, null!, null, null, logger));
        Assert.Throws<ArgumentNullException>(() => new GameNightModule(repository, scheduledEventService, null, null, null!));
    }

    [Fact]
    public void CreateCommand_IsExposedAsSlashCommand()
    {
        var attr = typeof(GameNightModule).GetMethod(nameof(GameNightModule.CreateAsync))!
            .GetCustomAttribute<SlashCommandAttribute>();
        Assert.NotNull(attr);
        Assert.Equal("create", attr!.Name);
    }

    [Fact]
    public void RescheduleCommand_IsExposedAsSlashCommand()
    {
        var attr = typeof(GameNightModule).GetMethod(nameof(GameNightModule.RescheduleAsync))!
            .GetCustomAttribute<SlashCommandAttribute>();
        Assert.NotNull(attr);
        Assert.Equal("reschedule", attr!.Name);
    }

    [Fact]
    public void CloseCommand_IsExposedAsSlashCommand()
    {
        var attr = typeof(GameNightModule).GetMethod(nameof(GameNightModule.CloseAsync))!
            .GetCustomAttribute<SlashCommandAttribute>();
        Assert.NotNull(attr);
        Assert.Equal("close", attr!.Name);
    }

    [Fact]
    public async Task CreateAsync_ReportsNotAllowed_WhenWrongGuild()
    {
        var serversOptions = Microsoft.Extensions.Options.Options.Create(new ServersOptions { Wk7ServerId = "999" });
        var module = CreateModule(serversOptions: serversOptions, guildId: 1);

        await module.CreateAsync();

        Assert.Equal(1, module.DeferCount);
        Assert.Single(module.Followups);
        Assert.Contains("nur auf dem WK7-Server", module.Followups[0]);
    }

    [Fact]
    public async Task CreateAsync_ReportsNoGuild_WhenContextGuildMissing()
    {
        var module = CreateModule();
        var context = DiscordTestDoubles.CreateContext();
        DiscordTestDoubles.SetUser(context, "Tester", 100);
        ((IInteractionModuleBase)module).SetContext(context);

        await module.CreateAsync();

        Assert.Equal(1, module.DeferCount);
        Assert.Single(module.Followups);
        Assert.Contains("nur innerhalb eines Servers", module.Followups[0]);
    }

    [Fact]
    public async Task CreateAsync_CreatesGameNight_WithSlotsAndTitle()
    {
        var repository = new Mock<IGameNightRepository>();
        GameNight? saved = null;
        repository.Setup(r => r.AddAsync(It.IsAny<GameNight>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((GameNight gn, CancellationToken _) => { saved = gn; gn.Id = 1; return gn; });
        repository.Setup(r => r.AddSlotsAsync(1, It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<GameNightSlot> { new() { Id = 1, GameNightId = 1, Label = "Fri 20:00", Position = 0 } });
        repository.Setup(r => r.GetSlotsAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<GameNightSlot> { new() { Id = 1, GameNightId = 1, Label = "Fri 20:00", Position = 0 } });
        repository.Setup(r => r.SetMessageAsync(1, It.IsAny<ulong>(), It.IsAny<ulong>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var module = CreateModule(repository: repository);

        await module.CreateAsync("Fri 20:00, Sat 19:00", "Test Night");

        Assert.Equal(1, module.DeferCount);
        Assert.Single(module.Followups);
        Assert.Contains("Game Night erstellt", module.Followups[0]);
        Assert.NotNull(saved);
        Assert.Equal("Test Night", saved!.Title);
        Assert.Equal(100UL, saved.OrganizerId);
        Assert.Single(module.Posts);
        repository.Verify(r => r.AddSlotsAsync(1, It.Is<IEnumerable<string>>(s => s.Count() == 2), It.IsAny<CancellationToken>()), Times.Once);
        repository.Verify(r => r.SetMessageAsync(1, 98765UL, 12345UL, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateAsync_RollsBack_AndReportsError_WhenPostFails()
    {
        var repository = new Mock<IGameNightRepository>();
        repository.Setup(r => r.AddAsync(It.IsAny<GameNight>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((GameNight gn, CancellationToken _) => { gn.Id = 1; return gn; });
        repository.Setup(r => r.GetSlotsAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<GameNightSlot>());
        repository.Setup(r => r.RemoveAsync(1, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var module = CreateModule(repository: repository);
        module.ThrowOnPost = true;

        await module.CreateAsync();

        repository.Verify(r => r.RemoveAsync(1, It.IsAny<CancellationToken>()), Times.Once);
        Assert.Single(module.Followups);
        Assert.Contains("konnte nicht erstellt werden", module.Followups[0]);
    }

    [Fact]
    public async Task CreateAsync_ReportsError_WhenRepositoryThrows()
    {
        var repository = new Mock<IGameNightRepository>();
        repository.Setup(r => r.AddAsync(It.IsAny<GameNight>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("db down"));

        var module = CreateModule(repository: repository);

        await module.CreateAsync();

        Assert.Equal(1, module.DeferCount);
        Assert.Single(module.Followups);
        Assert.Contains("konnte nicht erstellt werden", module.Followups[0]);
    }

    [Fact]
    public async Task RescheduleAsync_ReportsNotAllowed_WhenWrongGuild()
    {
        var serversOptions = Microsoft.Extensions.Options.Options.Create(new ServersOptions { Wk7ServerId = "999" });
        var module = CreateModule(serversOptions: serversOptions, guildId: 1);

        await module.RescheduleAsync("Fri 20:00");

        Assert.Equal(1, module.DeferCount);
        Assert.Single(module.Followups);
        Assert.Contains("Nur auf WK7-Server", module.Followups[0]);
    }

    [Fact]
    public async Task RescheduleAsync_ReportsNotFound_WhenGameNightMissing()
    {
        var repository = new Mock<IGameNightRepository>();
        repository.Setup(r => r.GetAsync(It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync((GameNight?)null);
        var module = CreateModule(repository: repository);

        await module.RescheduleAsync("Fri 20:00", 42);

        Assert.Equal(1, module.DeferCount);
        Assert.Single(module.Followups);
        Assert.Contains("nicht gefunden", module.Followups[0]);
    }

    [Fact]
    public async Task RescheduleAsync_ReportsNotOrganizer()
    {
        var repository = new Mock<IGameNightRepository>();
        repository.Setup(r => r.GetAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GameNight { Id = 1, OrganizerId = 999 });
        var module = CreateModule(repository: repository, userId: 100);

        await module.RescheduleAsync("Fri 20:00", 1);

        Assert.Equal(1, module.DeferCount);
        Assert.Single(module.Followups);
        Assert.Contains("Nur der Organisator", module.Followups[0]);
    }

    [Fact]
    public async Task RescheduleAsync_SetsExistingSlot()
    {
        var repository = new Mock<IGameNightRepository>();
        repository.Setup(r => r.GetAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GameNight { Id = 1, OrganizerId = 100 });
        repository.Setup(r => r.GetSlotsAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<GameNightSlot> { new() { Id = 5, GameNightId = 1, Label = "Fri 20:00" } });
        repository.Setup(r => r.SetBestSlotAsync(1, 5, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        repository.Setup(r => r.CloseAsync(1, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var module = CreateModule(repository: repository, userId: 100);

        await module.RescheduleAsync("Fri 20:00", 1);

        Assert.Equal(1, module.DeferCount);
        Assert.Single(module.Followups);
        Assert.Contains("verschoben", module.Followups[0]);
        repository.Verify(r => r.SetBestSlotAsync(1, 5, It.IsAny<CancellationToken>()), Times.Once);
        repository.Verify(r => r.CloseAsync(1, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RescheduleAsync_AddsNewSlot_WhenLabelUnknown()
    {
        var repository = new Mock<IGameNightRepository>();
        repository.Setup(r => r.GetAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GameNight { Id = 1, OrganizerId = 100 });
        repository.Setup(r => r.GetSlotsAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(new List<GameNightSlot>());
        repository.Setup(r => r.AddSlotAsync(It.IsAny<GameNightSlot>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((GameNightSlot s, CancellationToken _) => { s.Id = 7; return s; });
        repository.Setup(r => r.SetBestSlotAsync(1, 7, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        repository.Setup(r => r.CloseAsync(1, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var module = CreateModule(repository: repository, userId: 100);

        await module.RescheduleAsync("Sun 18:00", 1);

        Assert.Single(module.Followups);
        Assert.Contains("verschoben", module.Followups[0]);
        repository.Verify(r => r.AddSlotAsync(It.Is<GameNightSlot>(s => s.Label == "Sun 18:00"), It.IsAny<CancellationToken>()), Times.Once);
        repository.Verify(r => r.SetBestSlotAsync(1, 7, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CloseAsync_ReportsNotAllowed_WhenWrongGuild()
    {
        var serversOptions = Microsoft.Extensions.Options.Options.Create(new ServersOptions { Wk7ServerId = "999" });
        var module = CreateModule(serversOptions: serversOptions, guildId: 1);

        await module.CloseAsync();

        Assert.Equal(1, module.DeferCount);
        Assert.Single(module.Followups);
        Assert.Contains("Nur auf WK7-Server", module.Followups[0]);
    }

    [Fact]
    public async Task CloseAsync_ReportsNotFound_WhenGameNightMissing()
    {
        var repository = new Mock<IGameNightRepository>();
        repository.Setup(r => r.GetAsync(It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync((GameNight?)null);
        var module = CreateModule(repository: repository);

        await module.CloseAsync(42);

        Assert.Equal(1, module.DeferCount);
        Assert.Single(module.Followups);
        Assert.Contains("nicht gefunden", module.Followups[0]);
    }

    [Fact]
    public async Task CloseAsync_ReportsNotOrganizer()
    {
        var repository = new Mock<IGameNightRepository>();
        repository.Setup(r => r.GetAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GameNight { Id = 1, OrganizerId = 999 });
        var module = CreateModule(repository: repository, userId: 100);

        await module.CloseAsync(1);

        Assert.Equal(1, module.DeferCount);
        Assert.Single(module.Followups);
        Assert.Contains("Nur der Organisator", module.Followups[0]);
    }

    [Fact]
    public async Task CloseAsync_ClosesGameNight_AndCreatesEventAndReminders()
    {
        var repository = new Mock<IGameNightRepository>();
        repository.Setup(r => r.GetAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GameNight { Id = 1, OrganizerId = 100, GuildId = 1, ChannelId = 1, ClosesAt = DateTime.UtcNow.AddHours(1) });
        repository.Setup(r => r.GetSlotsAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<GameNightSlot>
            {
                new() { Id = 5, GameNightId = 1, Label = "Fri 20:00", Position = 0, SlotTime = DateTime.UtcNow.AddHours(2) }
            });
        repository.Setup(r => r.GetVotesAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<GameNightVote> { new() { GameNightId = 1, SlotId = 5, UserId = 200, Available = true } });
        repository.Setup(r => r.SetBestSlotAsync(1, 5, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        repository.Setup(r => r.CloseAsync(1, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var scheduledEventService = new Mock<IDiscordScheduledEventService>();
        scheduledEventService.Setup(s => s.CreateAsync(
                It.IsAny<IGuild>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<DateTimeOffset>(),
                It.IsAny<DateTimeOffset>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(111UL);

        var reminderRepository = new Mock<IReminderRepository>();
        reminderRepository.Setup(r => r.AddAsync(It.IsAny<Reminder>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var module = CreateModule(repository: repository, scheduledEventService: scheduledEventService, reminderRepository: reminderRepository, userId: 100);

        await module.CloseAsync(1);

        Assert.Equal(1, module.DeferCount);
        Assert.Single(module.Followups);
        Assert.Contains("geschlossen", module.Followups[0]);
        repository.Verify(r => r.SetBestSlotAsync(1, 5, It.IsAny<CancellationToken>()), Times.Once);
        repository.Verify(r => r.CloseAsync(1, It.IsAny<CancellationToken>()), Times.Once);
        scheduledEventService.Verify(s => s.CreateAsync(
            It.IsAny<IGuild>(),
            It.Is<string>(n => n.Contains("Test Night") || n.Contains("Spieleabend")),
            It.IsAny<string?>(),
            It.Is<string?>(l => l == "Fri 20:00"),
            It.IsAny<DateTimeOffset>(),
            It.IsAny<DateTimeOffset>(),
            "game_night",
            It.IsAny<CancellationToken>()), Times.Once);
        reminderRepository.Verify(r => r.AddAsync(It.Is<Reminder>(rem => rem.DiscordUserId == 200UL), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CloseAsync_ReportsError_WhenRepositoryThrows()
    {
        var repository = new Mock<IGameNightRepository>();
        repository.Setup(r => r.GetAsync(It.IsAny<int>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("db down"));
        var module = CreateModule(repository: repository);

        await module.CloseAsync(1);

        Assert.Equal(1, module.DeferCount);
        Assert.Single(module.Followups);
        Assert.Contains("Schließen fehlgeschlagen", module.Followups[0]);
    }
}
