using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using WK7Bot.Core.Utilities;
using WK7Bot.Services;
using WK7Bot.Services.Interfaces;
using Xunit;

namespace WK7Bot.Tests;

public class DealWatchlistBackgroundServiceTests
{
    private sealed class TestableService : DealWatchlistBackgroundService
    {
        public TestableService(
            IServiceScopeFactory scopeFactory,
            IFeatureHealthTracker health,
            FeatureGate featureGate,
            Wk7BotOptions options)
            : base(
                scopeFactory,
                health,
                featureGate,
                Microsoft.Extensions.Options.Options.Create(options),
                NullLogger<DealWatchlistBackgroundService>.Instance)
        {
        }

        public Task ExecuteAsyncPublic(CancellationToken cancellationToken) => ExecuteAsync(cancellationToken);
    }

    private static (IServiceScopeFactory Factory, Mock<IDealWatchlistService> ServiceMock) CreateScopeFactoryWithService()
    {
        var serviceMock = new Mock<IDealWatchlistService>();
        var services = new ServiceCollection();
        services.AddScoped(_ => serviceMock.Object);
        var provider = services.BuildServiceProvider();
        return (new ServiceProviderScopeFactory(provider), serviceMock);
    }

    [Fact]
    public async Task ExecuteAsync_WhenDisabled_ReturnsImmediately_AndDoesNotRecordHealth()
    {
        var health = new FeatureHealthTracker();
        var featureGate = new FeatureGate(Microsoft.Extensions.Options.Options.Create(new Wk7BotOptions
        {
            Features = new FeatureOptions { DealWatchlistEnabled = false }
        }));
        var service = new TestableService(
            new Mock<IServiceScopeFactory>().Object,
            health,
            featureGate,
            new Wk7BotOptions());

        await service.ExecuteAsyncPublic(CancellationToken.None);

        Assert.False(health.Get(FeatureKeys.DealWatchlist).HasRun);
    }

    [Fact]
    public async Task ExecuteAsync_WhenEnabled_RecordsSuccess()
    {
        var health = new FeatureHealthTracker();
        var featureGate = new FeatureGate(Microsoft.Extensions.Options.Options.Create(new Wk7BotOptions()));
        var (scopeFactory, serviceMock) = CreateScopeFactoryWithService();
        serviceMock
            .Setup(s => s.CheckAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        var service = new TestableService(scopeFactory, health, featureGate, new Wk7BotOptions());

        using var cts = new CancellationTokenSource();
        var executeTask = service.ExecuteAsyncPublic(cts.Token);
        cts.CancelAfter(TimeSpan.FromMilliseconds(100));
        await executeTask;

        var snapshot = health.Get(FeatureKeys.DealWatchlist);
        Assert.True(snapshot.HasRun);
        Assert.False(snapshot.IsProblem);
        serviceMock.Verify(s => s.CheckAllAsync(It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    [Fact]
    public async Task ExecuteAsync_ServiceThrows_RecordsFailure()
    {
        var health = new FeatureHealthTracker();
        var featureGate = new FeatureGate(Microsoft.Extensions.Options.Options.Create(new Wk7BotOptions()));
        var (scopeFactory, serviceMock) = CreateScopeFactoryWithService();
        serviceMock
            .Setup(s => s.CheckAllAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("watchlist service failed"));

        var service = new TestableService(scopeFactory, health, featureGate, new Wk7BotOptions());

        using var cts = new CancellationTokenSource();
        var executeTask = service.ExecuteAsyncPublic(cts.Token);
        cts.CancelAfter(TimeSpan.FromMilliseconds(100));
        await executeTask;

        var snapshot = health.Get(FeatureKeys.DealWatchlist);
        Assert.True(snapshot.HasRun);
        Assert.True(snapshot.IsProblem);
        Assert.Contains("watchlist service failed", snapshot.LastError);
    }
}
