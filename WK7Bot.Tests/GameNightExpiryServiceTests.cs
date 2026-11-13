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

public class GameNightExpiryServiceTests
{
    private sealed class TestableService : GameNightExpiryService
    {
        public TestableService(
            IServiceScopeFactory scopeFactory,
            IFeatureHealthTracker health,
            Wk7BotOptions options)
            : base(
                scopeFactory,
                DiscordTestDoubles.CreateClient(),
                health,
                Microsoft.Extensions.Options.Options.Create(options),
                NullLogger<GameNightExpiryService>.Instance)
        {
        }

        public Task ExecuteAsyncPublic(CancellationToken cancellationToken) => ExecuteAsync(cancellationToken);
    }

    private static (IServiceScopeFactory Factory, Mock<IGameNightRepository> RepoMock) CreateScopeFactoryWithRepo()
    {
        var repoMock = new Mock<IGameNightRepository>();
        var services = new ServiceCollection();
        services.AddScoped(_ => repoMock.Object);
        var provider = services.BuildServiceProvider();
        return (new ServiceProviderScopeFactory(provider), repoMock);
    }

    [Fact]
    public async Task ExecuteAsync_WhenDisabled_ReturnsImmediately_AndDoesNotRecordHealth()
    {
        var health = new FeatureHealthTracker();
        var service = new TestableService(
            new Mock<IServiceScopeFactory>().Object,
            health,
            new Wk7BotOptions
            {
                Features = new FeatureOptions { GameNightEnabled = false }
            });

        await service.ExecuteAsyncPublic(CancellationToken.None);

        Assert.False(health.Get(FeatureKeys.GameNight).HasRun);
    }

    [Fact]
    public async Task ExecuteAsync_NoExpiredGameNights_RecordsSuccess()
    {
        var health = new FeatureHealthTracker();
        var (scopeFactory, repoMock) = CreateScopeFactoryWithRepo();
        repoMock
            .Setup(r => r.GetExpiredAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<GameNight>());

        var service = new TestableService(scopeFactory, health, new Wk7BotOptions());

        using var cts = new CancellationTokenSource();
        var executeTask = service.ExecuteAsyncPublic(cts.Token);
        cts.CancelAfter(TimeSpan.FromMilliseconds(100));
        await executeTask;

        var snapshot = health.Get(FeatureKeys.GameNight);
        Assert.True(snapshot.HasRun);
        Assert.False(snapshot.IsProblem);
    }

    [Fact]
    public async Task ExecuteAsync_RepositoryThrows_RecordsFailure()
    {
        var health = new FeatureHealthTracker();
        var (scopeFactory, repoMock) = CreateScopeFactoryWithRepo();
        repoMock
            .Setup(r => r.GetExpiredAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("database unavailable"));

        var service = new TestableService(scopeFactory, health, new Wk7BotOptions());

        using var cts = new CancellationTokenSource();
        var executeTask = service.ExecuteAsyncPublic(cts.Token);
        cts.CancelAfter(TimeSpan.FromMilliseconds(100));
        await executeTask;

        var snapshot = health.Get(FeatureKeys.GameNight);
        Assert.True(snapshot.HasRun);
        Assert.True(snapshot.IsProblem);
        Assert.Contains("database unavailable", snapshot.LastError);
    }

    [Fact]
    public async Task ExecuteAsync_ExpiredGameNightWithNoSlots_RecordsSuccess()
    {
        var health = new FeatureHealthTracker();
        var (scopeFactory, repoMock) = CreateScopeFactoryWithRepo();
        var gameNight = new GameNight
        {
            Id = 1,
            GuildId = 1,
            ChannelId = 100,
            MessageId = 200,
            Title = "Test Night",
            ClosesAt = DateTime.UtcNow.AddHours(-1)
        };

        repoMock
            .Setup(r => r.GetExpiredAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<GameNight> { gameNight });
        repoMock
            .Setup(r => r.GetSlotsAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<GameNightSlot>());
        repoMock
            .Setup(r => r.GetVotesAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<GameNightVote>());

        var service = new TestableService(scopeFactory, health, new Wk7BotOptions());

        using var cts = new CancellationTokenSource();
        var executeTask = service.ExecuteAsyncPublic(cts.Token);
        cts.CancelAfter(TimeSpan.FromMilliseconds(100));
        await executeTask;

        repoMock.Verify(r => r.CloseManyAsync(It.Is<IEnumerable<int>>(ids => ids.Contains(1)), It.IsAny<CancellationToken>()), Times.Once);
        Assert.False(health.Get(FeatureKeys.GameNight).IsProblem);
    }
}
