using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using WK7Bot.Core.Utilities;
using WK7Bot.Services;
using WK7Bot.Services.Interfaces;
using Xunit;

namespace WK7Bot.Tests;

public class LeaderboardBackgroundServiceTests
{
    private sealed class TestableService : LeaderboardBackgroundService
    {
        public TestableService(
            DiscordSocketClient discordClient,
            IServiceScopeFactory scopeFactory,
            IFeatureHealthTracker health,
            FeatureGate featureGate,
            Wk7BotOptions options)
            : base(
                discordClient,
                scopeFactory,
                health,
                featureGate,
                Microsoft.Extensions.Options.Options.Create(options),
                NullLogger<LeaderboardBackgroundService>.Instance)
        {
        }

        public Task ExecuteAsyncPublic(CancellationToken cancellationToken) => ExecuteAsync(cancellationToken);
    }

    private static (IServiceScopeFactory Factory, Mock<ILeaderboardService> ServiceMock) CreateScopeFactoryWithService()
    {
        var serviceMock = new Mock<ILeaderboardService>();
        var services = new ServiceCollection();
        services.AddScoped(_ => serviceMock.Object);
        var provider = services.BuildServiceProvider();
        return (new ServiceProviderScopeFactory(provider), serviceMock);
    }

    private static Wk7BotOptions OptionsForToday()
    {
        var now = DateTime.UtcNow;
        return new Wk7BotOptions
        {
            LeaderboardPostWeekday = now.DayOfWeek.ToString(),
            LeaderboardPostTime = "00:00"
        };
    }

    [Fact]
    public async Task ExecuteAsync_WhenDisabled_ReturnsImmediately_AndDoesNotRecordHealth()
    {
        var health = new FeatureHealthTracker();
        var featureGate = new FeatureGate(Microsoft.Extensions.Options.Options.Create(new Wk7BotOptions
        {
            Features = new FeatureOptions { LeaderboardEnabled = false }
        }));
        var service = new TestableService(
            DiscordTestDoubles.CreateClient(),
            new Mock<IServiceScopeFactory>().Object,
            health,
            featureGate,
            new Wk7BotOptions());

        await service.ExecuteAsyncPublic(CancellationToken.None);

        Assert.False(health.Get(FeatureKeys.Leaderboard).HasRun);
    }

    [Fact]
    public async Task ExecuteAsync_WhenNoGuilds_RecordsSuccess()
    {
        var health = new FeatureHealthTracker();
        var featureGate = new FeatureGate(Microsoft.Extensions.Options.Options.Create(new Wk7BotOptions()));
        var (scopeFactory, _) = CreateScopeFactoryWithService();
        var service = new TestableService(
            DiscordTestDoubles.CreateClient(),
            scopeFactory,
            health,
            featureGate,
            OptionsForToday());

        using var cts = new CancellationTokenSource();
        var executeTask = service.ExecuteAsyncPublic(cts.Token);
        cts.CancelAfter(TimeSpan.FromMilliseconds(100));
        await executeTask;

        var snapshot = health.Get(FeatureKeys.Leaderboard);
        Assert.True(snapshot.HasRun);
        Assert.False(snapshot.IsProblem);
    }

    [Fact]
    public async Task ExecuteAsync_ScopeFactoryThrows_RecordsFailure()
    {
        var health = new FeatureHealthTracker();
        var featureGate = new FeatureGate(Microsoft.Extensions.Options.Options.Create(new Wk7BotOptions()));
        var mockScopeFactory = new Mock<IServiceScopeFactory>();
        mockScopeFactory.Setup(f => f.CreateScope()).Throws<InvalidOperationException>();

        var service = new TestableService(
            DiscordTestDoubles.CreateClient(),
            mockScopeFactory.Object,
            health,
            featureGate,
            OptionsForToday());

        using var cts = new CancellationTokenSource();
        var executeTask = service.ExecuteAsyncPublic(cts.Token);
        cts.CancelAfter(TimeSpan.FromMilliseconds(100));
        await executeTask;

        var snapshot = health.Get(FeatureKeys.Leaderboard);
        Assert.True(snapshot.HasRun);
        Assert.True(snapshot.IsProblem);
        Assert.NotNull(snapshot.LastError);
    }

    [Fact]
    public async Task ExecuteAsync_WrongWeekday_DoesNotCallLeaderboardService()
    {
        var health = new FeatureHealthTracker();
        var featureGate = new FeatureGate(Microsoft.Extensions.Options.Options.Create(new Wk7BotOptions()));
        var (scopeFactory, serviceMock) = CreateScopeFactoryWithService();
        var options = new Wk7BotOptions
        {
            LeaderboardPostWeekday = DateTime.UtcNow.AddDays(1).DayOfWeek.ToString(),
            LeaderboardPostTime = "00:00"
        };
        var service = new TestableService(
            DiscordTestDoubles.CreateClient(),
            scopeFactory,
            health,
            featureGate,
            options);

        using var cts = new CancellationTokenSource();
        var executeTask = service.ExecuteAsyncPublic(cts.Token);
        cts.CancelAfter(TimeSpan.FromMilliseconds(100));
        await executeTask;

        serviceMock.Verify(
            s => s.GetAsync(It.IsAny<string>(), It.IsAny<ulong>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
        Assert.False(health.Get(FeatureKeys.Leaderboard).IsProblem);
    }
}
