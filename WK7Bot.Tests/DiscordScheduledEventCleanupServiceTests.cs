using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using WK7Bot.Core.Utilities;
using WK7Bot.Services;
using WK7Bot.Services.Interfaces;
using Xunit;

namespace WK7Bot.Tests;

public class DiscordScheduledEventCleanupServiceTests
{
    private sealed class TestableService : DiscordScheduledEventCleanupService
    {
        public TestableService(
            IDiscordScheduledEventService scheduledEventService,
            IFeatureHealthTracker health)
            : base(
                scheduledEventService,
                health,
                NullLogger<DiscordScheduledEventCleanupService>.Instance)
        {
        }

        public Task ExecuteAsyncPublic(CancellationToken cancellationToken) => ExecuteAsync(cancellationToken);
    }

    [Fact]
    public async Task ExecuteAsync_NoExpiredEvents_RecordsSuccess()
    {
        var health = new FeatureHealthTracker();
        var serviceMock = new Mock<IDiscordScheduledEventService>();
        serviceMock
            .Setup(s => s.DeleteExpiredAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        var service = new TestableService(serviceMock.Object, health);

        using var cts = new CancellationTokenSource();
        var executeTask = service.ExecuteAsyncPublic(cts.Token);
        cts.CancelAfter(TimeSpan.FromMilliseconds(100));
        await executeTask;

        var snapshot = health.Get(FeatureKeys.DiscordEvents);
        Assert.True(snapshot.HasRun);
        Assert.False(snapshot.IsProblem);
    }

    [Fact]
    public async Task ExecuteAsync_DeletesExpiredEvents_RecordsSuccess()
    {
        var health = new FeatureHealthTracker();
        var serviceMock = new Mock<IDiscordScheduledEventService>();
        serviceMock
            .Setup(s => s.DeleteExpiredAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(3);

        var service = new TestableService(serviceMock.Object, health);

        using var cts = new CancellationTokenSource();
        var executeTask = service.ExecuteAsyncPublic(cts.Token);
        cts.CancelAfter(TimeSpan.FromMilliseconds(100));
        await executeTask;

        serviceMock.Verify(s => s.DeleteExpiredAsync(It.IsAny<CancellationToken>()), Times.AtLeastOnce);
        Assert.False(health.Get(FeatureKeys.DiscordEvents).IsProblem);
    }

    [Fact]
    public async Task ExecuteAsync_ServiceThrows_RecordsFailure()
    {
        var health = new FeatureHealthTracker();
        var serviceMock = new Mock<IDiscordScheduledEventService>();
        serviceMock
            .Setup(s => s.DeleteExpiredAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Discord API error"));

        var service = new TestableService(serviceMock.Object, health);

        using var cts = new CancellationTokenSource();
        var executeTask = service.ExecuteAsyncPublic(cts.Token);
        cts.CancelAfter(TimeSpan.FromMilliseconds(100));
        await executeTask;

        var snapshot = health.Get(FeatureKeys.DiscordEvents);
        Assert.True(snapshot.HasRun);
        Assert.True(snapshot.IsProblem);
        Assert.Contains("Discord API error", snapshot.LastError);
    }

    [Fact]
    public void Constructor_NullDependencies_ThrowArgumentNullException()
    {
        var health = new FeatureHealthTracker();
        var serviceMock = Mock.Of<IDiscordScheduledEventService>();

        Assert.Throws<ArgumentNullException>(() => new DiscordScheduledEventCleanupService(null!, health, NullLogger<DiscordScheduledEventCleanupService>.Instance));
        Assert.Throws<ArgumentNullException>(() => new DiscordScheduledEventCleanupService(serviceMock, null!, NullLogger<DiscordScheduledEventCleanupService>.Instance));
        Assert.Throws<ArgumentNullException>(() => new DiscordScheduledEventCleanupService(serviceMock, health, null!));
    }
}
