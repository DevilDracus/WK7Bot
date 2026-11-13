using System.Reflection;
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

public class BirthdayModuleTests
{
    private sealed class TestBirthdayModule : BirthdayModule
    {
        public int DeferCount { get; private set; }
        public List<string> Followups { get; } = new();

        public TestBirthdayModule(IBirthdayRepository repository)
            : base(repository, NullLogger<BirthdayModule>.Instance)
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
    }

    private static TestBirthdayModule CreateModule(Mock<IBirthdayRepository> repository, ulong userId = 42)
    {
        var module = new TestBirthdayModule(repository.Object);
        var context = DiscordTestDoubles.CreateContext();
        DiscordTestDoubles.SetUser(context, "Tester", userId);
        ((IInteractionModuleBase)module).SetContext(context);
        return module;
    }

    [Fact]
    public void Constructor_Throws_WhenDependenciesAreNull()
    {
        Assert.Throws<ArgumentNullException>(() => new BirthdayModule(null!, NullLogger<BirthdayModule>.Instance));
        Assert.Throws<ArgumentNullException>(() => new BirthdayModule(new Mock<IBirthdayRepository>().Object, null!));
    }

    [Fact]
    public void SetCommand_IsExposedAsSlashCommand()
    {
        var attr = typeof(BirthdayModule).GetMethod(nameof(BirthdayModule.SetAsync))!
            .GetCustomAttribute<SlashCommandAttribute>();
        Assert.NotNull(attr);
        Assert.Equal("birthday-set", attr!.Name);
    }

    [Fact]
    public void AddBirthdayCommand_IsExposedAsSlashCommand()
    {
        var attr = typeof(BirthdayModule).GetMethod(nameof(BirthdayModule.AddBirthdayAsync))!
            .GetCustomAttribute<SlashCommandAttribute>();
        Assert.NotNull(attr);
        Assert.Equal("add-birthday", attr!.Name);
    }

    [Fact]
    public void ShowCommand_IsExposedAsSlashCommand()
    {
        var attr = typeof(BirthdayModule).GetMethod(nameof(BirthdayModule.ShowAsync))!
            .GetCustomAttribute<SlashCommandAttribute>();
        Assert.NotNull(attr);
        Assert.Equal("birthday-show", attr!.Name);
    }

    [Fact]
    public void RemoveCommand_IsExposedAsSlashCommand()
    {
        var attr = typeof(BirthdayModule).GetMethod(nameof(BirthdayModule.RemoveAsync))!
            .GetCustomAttribute<SlashCommandAttribute>();
        Assert.NotNull(attr);
        Assert.Equal("birthday-remove", attr!.Name);
    }

    [Fact]
    public async Task SetAsync_StoresBirthday_WhenDateIsValid()
    {
        var repository = new Mock<IBirthdayRepository>();
        var module = CreateModule(repository);

        await module.SetAsync(15, 6);

        Assert.Equal(1, module.DeferCount);
        repository.Verify(r => r.UpsertAsync(It.Is<Birthday>(b => b.Day == 15 && b.Month == 6 && b.DiscordUserId == 42), It.IsAny<CancellationToken>()), Times.Once);
        Assert.Single(module.Followups);
        Assert.Contains("Geburtstag gesetzt", module.Followups[0]);
    }

    [Fact]
    public async Task SetAsync_RejectsInvalidDate()
    {
        var repository = new Mock<IBirthdayRepository>();
        var module = CreateModule(repository);

        await module.SetAsync(31, 2);

        Assert.Equal(1, module.DeferCount);
        repository.Verify(r => r.UpsertAsync(It.IsAny<Birthday>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Single(module.Followups);
        Assert.Contains("Ungültiges Datum", module.Followups[0]);
    }

    [Fact]
    public async Task SetAsync_ReportsError_WhenRepositoryThrows()
    {
        var repository = new Mock<IBirthdayRepository>();
        repository.Setup(r => r.UpsertAsync(It.IsAny<Birthday>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("db down"));
        var module = CreateModule(repository);

        await module.SetAsync(10, 10);

        repository.Verify(r => r.UpsertAsync(It.IsAny<Birthday>(), It.IsAny<CancellationToken>()), Times.Once);
        Assert.Single(module.Followups);
        Assert.Contains("konnte nicht gespeichert werden", module.Followups[0]);
    }

    [Fact]
    public async Task ShowAsync_ReportsStoredBirthday()
    {
        var repository = new Mock<IBirthdayRepository>();
        repository.Setup(r => r.GetForUserAsync(42, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Birthday { DiscordUserId = 42, Day = 3, Month = 7 });
        var module = CreateModule(repository);

        await module.ShowAsync();

        Assert.Equal(1, module.DeferCount);
        Assert.Single(module.Followups);
        Assert.Contains("03.07", module.Followups[0]);
    }

    [Fact]
    public async Task ShowAsync_ReportsMissingBirthday()
    {
        var repository = new Mock<IBirthdayRepository>();
        repository.Setup(r => r.GetForUserAsync(42, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Birthday?)null);
        var module = CreateModule(repository);

        await module.ShowAsync();

        Assert.Single(module.Followups);
        Assert.Contains("noch keinen Geburtstag", module.Followups[0]);
    }

    [Fact]
    public async Task RemoveAsync_ReportsResult()
    {
        var repository = new Mock<IBirthdayRepository>();
        repository.Setup(r => r.RemoveAsync(42, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var module = CreateModule(repository);

        await module.RemoveAsync();

        Assert.Equal(1, module.DeferCount);
        repository.Verify(r => r.RemoveAsync(42, It.IsAny<CancellationToken>()), Times.Once);
        Assert.Single(module.Followups);
        Assert.Contains("Geburtstag entfernt", module.Followups[0]);
    }
}
