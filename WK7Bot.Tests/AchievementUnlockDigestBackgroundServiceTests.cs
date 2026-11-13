using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using WK7Bot.Core.Entities;
using WK7Bot.Core.Interfaces;
using WK7Bot.Core.Utilities;
using WK7Bot.Models;
using WK7Bot.Services;
using WK7Bot.Services.Interfaces;
using Xunit;

namespace WK7Bot.Tests;

public class AchievementUnlockDigestBackgroundServiceTests
{
    private static (IServiceScopeFactory Factory, Mock<IUserAccountLinkRepository> LinkRepo, Mock<ISteamAchievementBaselineRepository> BaselineRepo) CreateScopeFactoryWithMocks()
    {
        var linkRepo = new Mock<IUserAccountLinkRepository>();
        var baselineRepo = new Mock<ISteamAchievementBaselineRepository>();

        var link = linkRepo.Object;
        var baseline = baselineRepo.Object;
        var services = new ServiceCollection();
        services.AddScoped(_ => link);
        services.AddScoped(_ => baseline);
        var provider = services.BuildServiceProvider();
        return (new ServiceProviderScopeFactory(provider), linkRepo, baselineRepo);
    }

    private static IServiceScopeFactory CreateFailingScopeFactory()
    {
        var mock = new Mock<IServiceScopeFactory>();
        mock.Setup(f => f.CreateScope()).Throws<InvalidOperationException>();
        return mock.Object;
    }

    private sealed class TestableService : AchievementUnlockDigestBackgroundService
    {
        public TestableService(
            IServiceScopeFactory scopeFactory,
            IFeatureHealthTracker health,
            Wk7BotOptions options,
            ISteamService? steamService = null)
            : base(
                scopeFactory,
                DiscordTestDoubles.CreateClient(),
                steamService ?? Mock.Of<ISteamService>(),
                health,
                Microsoft.Extensions.Options.Options.Create(options),
                NullLogger<AchievementUnlockDigestBackgroundService>.Instance)
        {
        }

        public Task ExecuteAsyncPublic(CancellationToken cancellationToken) => ExecuteAsync(cancellationToken);
    }

    [Fact]
    public async Task ExecuteAsync_WithoutSteamApiKey_ReturnsImmediately_AndDoesNotRecordHealth()
    {
        var health = new FeatureHealthTracker();
        var service = new TestableService(
            CreateFailingScopeFactory(),
            health,
            new Wk7BotOptions { SteamApiKey = string.Empty });

        await service.ExecuteAsyncPublic(CancellationToken.None);

        Assert.False(health.Get("achievement_unlock_digest").HasRun);
    }

    [Fact]
    public async Task ExecuteAsync_WhenGuildUnavailable_RecordsSuccess()
    {
        var health = new FeatureHealthTracker();
        var (scopeFactory, _, _) = CreateScopeFactoryWithMocks();
        var service = new TestableService(
            scopeFactory,
            health,
            new Wk7BotOptions
            {
                SteamApiKey = "test-key",
                Servers = new ServersOptions { Wk7ServerId = "100" }
            });

        using var cts = new CancellationTokenSource();
        var executeTask = service.ExecuteAsyncPublic(cts.Token);
        cts.CancelAfter(TimeSpan.FromMilliseconds(100));
        await executeTask;

        var snapshot = health.Get("achievement_unlock_digest");
        Assert.True(snapshot.HasRun);
        Assert.False(snapshot.IsProblem);
    }

    [Fact]
    public async Task ExecuteAsync_IterationFailure_RecordsFailure()
    {
        var health = new FeatureHealthTracker();
        var service = new TestableService(
            CreateFailingScopeFactory(),
            health,
            new Wk7BotOptions
            {
                SteamApiKey = "test-key",
                Servers = null!
            });

        using var cts = new CancellationTokenSource();
        var executeTask = service.ExecuteAsyncPublic(cts.Token);
        cts.CancelAfter(TimeSpan.FromMilliseconds(100));
        await executeTask;

        var snapshot = health.Get("achievement_unlock_digest");
        Assert.True(snapshot.HasRun);
        Assert.True(snapshot.IsProblem);
        Assert.NotNull(snapshot.LastError);
    }
}
