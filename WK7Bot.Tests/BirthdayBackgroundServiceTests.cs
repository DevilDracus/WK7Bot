using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using WK7Bot.Core.Entities;
using WK7Bot.Core.Interfaces;
using WK7Bot.Core.Utilities;
using WK7Bot.Services;
using Xunit;

namespace WK7Bot.Tests;

public class BirthdayBackgroundServiceTests
{
    private sealed class TestableService : BirthdayBackgroundService
    {
        private readonly IReadOnlyList<ulong> _guildIds;

        public TestableService(
            IServiceScopeFactory scopeFactory,
            IFeatureHealthTracker health,
            Wk7BotOptions options,
            IReadOnlyList<ulong>? guildIds = null)
            : base(
                scopeFactory,
                DiscordTestDoubles.CreateClient(),
                health,
                Microsoft.Extensions.Options.Options.Create(options),
                NullLogger<BirthdayBackgroundService>.Instance)
            => _guildIds = guildIds ?? Array.Empty<ulong>();

        public Task ExecuteAsyncPublic(CancellationToken cancellationToken) => ExecuteAsync(cancellationToken);

        protected override IReadOnlyList<ulong> GetTargetGuildIds() => _guildIds;
    }

    private static (IServiceScopeFactory Factory, Mock<IBirthdayRepository> RepoMock) CreateScopeFactoryWithRepo()
    {
        var repoMock = new Mock<IBirthdayRepository>();
        var services = new ServiceCollection();
        services.AddScoped(_ => repoMock.Object);
        var provider = services.BuildServiceProvider();
        return (new ServiceProviderScopeFactory(provider), repoMock);
    }

    private static IServiceScopeFactory CreateFailingScopeFactory()
    {
        var mock = new Mock<IServiceScopeFactory>();
        mock.Setup(f => f.CreateScope()).Throws<InvalidOperationException>();
        return mock.Object;
    }

    [Fact]
    public async Task ExecuteAsync_WhenDisabled_ReturnsImmediately_AndDoesNotRecordHealth()
    {
        var health = new FeatureHealthTracker();
        var service = new TestableService(
            CreateFailingScopeFactory(),
            health,
            new Wk7BotOptions
            {
                Features = new FeatureOptions { BirthdayEnabled = false }
            });

        await service.ExecuteAsyncPublic(CancellationToken.None);

        Assert.False(health.Get(FeatureKeys.Birthdays).HasRun);
    }

    [Fact]
    public async Task ExecuteAsync_WhenEnabled_RecordsSuccess()
    {
        var health = new FeatureHealthTracker();
        var (scopeFactory, repoMock) = CreateScopeFactoryWithRepo();
        repoMock
            .Setup(r => r.GetDueTodayAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Birthday>());

        var service = new TestableService(scopeFactory, health, new Wk7BotOptions(), new ulong[] { 1 });

        using var cts = new CancellationTokenSource();
        var executeTask = service.ExecuteAsyncPublic(cts.Token);
        cts.CancelAfter(TimeSpan.FromMilliseconds(100));
        await executeTask;

        var snapshot = health.Get(FeatureKeys.Birthdays);
        Assert.True(snapshot.HasRun);
        Assert.False(snapshot.IsProblem);
    }

    [Fact]
    public async Task ExecuteAsync_RepositoryThrows_RecordsFailure()
    {
        var health = new FeatureHealthTracker();
        var (scopeFactory, repoMock) = CreateScopeFactoryWithRepo();
        repoMock
            .Setup(r => r.GetDueTodayAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("database unavailable"));

        var service = new TestableService(scopeFactory, health, new Wk7BotOptions(), new ulong[] { 1 });

        using var cts = new CancellationTokenSource();
        var executeTask = service.ExecuteAsyncPublic(cts.Token);
        cts.CancelAfter(TimeSpan.FromMilliseconds(100));
        await executeTask;

        var snapshot = health.Get(FeatureKeys.Birthdays);
        Assert.True(snapshot.HasRun);
        Assert.True(snapshot.IsProblem);
        Assert.Contains("database unavailable", snapshot.LastError);
    }

    [Fact]
    public async Task ExecuteAsync_DueBirthday_AnnouncesAndMarksAnnounced()
    {
        var health = new FeatureHealthTracker();
        var (scopeFactory, repoMock) = CreateScopeFactoryWithRepo();
        var birthday = new Birthday
        {
            Id = 42,
            DiscordUserId = 99,
            Month = 6,
            Day = 15
        };
        repoMock
            .Setup(r => r.GetDueTodayAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Birthday> { birthday });

        var service = new TestableService(scopeFactory, health, new Wk7BotOptions(), new ulong[] { 1 });

        using var cts = new CancellationTokenSource();
        var executeTask = service.ExecuteAsyncPublic(cts.Token);
        cts.CancelAfter(TimeSpan.FromMilliseconds(100));
        await executeTask;

        repoMock.Verify(
            r => r.MarkAnnouncedAsync(42, It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Once);
        Assert.False(health.Get(FeatureKeys.Birthdays).IsProblem);
    }
}
