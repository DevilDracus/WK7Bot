using System.Net;
using System.Net.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using WK7Bot.Core.Utilities;
using WK7Bot.Services;
using Xunit;

namespace WK7Bot.Tests;

public class BaseGameApiClientTests
{
    private sealed class TestableClient : BaseGameApiClient
    {
        private readonly SemaphoreSlim _throttle;
        private readonly ILogger _logger;
        private readonly int _maxAttempts;
        private readonly TimeSpan _retryDelay;

        public TestableClient(
            MetricsRegistry metrics,
            int maxAttempts = 3,
            int concurrency = 1,
            TimeSpan? retryDelay = null,
            bool disposeThrottle = true)
            : base(metrics)
        {
            _throttle = new SemaphoreSlim(concurrency, concurrency);
            _logger = NullLogger<TestableClient>.Instance;
            _maxAttempts = maxAttempts;
            _retryDelay = retryDelay ?? TimeSpan.Zero;
            DisposeApiThrottleOverride = disposeThrottle;
        }

        protected override int MaxAttempts => _maxAttempts;
        protected override SemaphoreSlim ApiThrottle => _throttle;
        protected override ILogger Logger => _logger;
        protected override string ServiceName => "Test";
        public bool DisposeApiThrottleOverride { get; }
        protected override bool DisposeApiThrottle => DisposeApiThrottleOverride;
        protected override TimeSpan RetryDelay => _retryDelay;
    }

    private static TestableClient CreateClient(int maxAttempts = 3)
        => new(new MetricsRegistry(), maxAttempts);

    [Fact]
    public void Constructor_NullMetrics_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new TestableClient(null!));
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests, true)]
    [InlineData(HttpStatusCode.InternalServerError, true)]
    [InlineData(HttpStatusCode.BadGateway, true)]
    [InlineData(HttpStatusCode.OK, false)]
    [InlineData(HttpStatusCode.NotFound, false)]
    [InlineData(HttpStatusCode.Forbidden, false)]
    public void IsTransientFailure_ClassifiesStatusCodesCorrectly(HttpStatusCode status, bool expected)
    {
        using var client = CreateClient();
        using var response = new HttpResponseMessage(status);
        Assert.Equal(expected, client.IsTransientFailure(response));
    }

    [Fact]
    public async Task SendThrottledAsync_InvokesFactory_AndReturnsResponse()
    {
        using var client = CreateClient();
        using var expected = new HttpResponseMessage(HttpStatusCode.OK);

        var response = await client.SendThrottledAsync(() => Task.FromResult(expected), CancellationToken.None);

        Assert.Same(expected, response);
    }

    [Fact]
    public async Task SendThrottledAsync_NullFactory_ThrowsArgumentNullException()
    {
        using var client = CreateClient();
        await Assert.ThrowsAsync<ArgumentNullException>(() => client.SendThrottledAsync(null!, CancellationToken.None));
    }

    [Fact]
    public async Task ExecuteWithRetryAsync_RetriesOnTransientFailure_ThenSucceeds()
    {
        using var client = CreateClient(maxAttempts: 3);
        var attempt = 0;

        using var response = await client.ExecuteWithRetryAsync(() =>
        {
            attempt++;
            return Task.FromResult(new HttpResponseMessage(attempt < 3 ? HttpStatusCode.TooManyRequests : HttpStatusCode.OK));
        }, "test-op", CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(3, attempt);
    }

    [Fact]
    public async Task ExecuteWithRetryAsync_NonTransientResponse_ReturnsWithoutRetry()
    {
        using var client = CreateClient(maxAttempts: 3);
        var attempt = 0;

        using var response = await client.ExecuteWithRetryAsync(() =>
        {
            attempt++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }, "test-op", CancellationToken.None);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(1, attempt);
    }

    [Fact]
    public async Task ExecuteWithRetryAsync_AllAttemptsFail_ReturnsLastTransientResponse()
    {
        using var client = CreateClient(maxAttempts: 2);
        var attempt = 0;

        using var response = await client.ExecuteWithRetryAsync(() =>
        {
            attempt++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        }, "test-op", CancellationToken.None);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(2, attempt);
    }

    [Fact]
    public async Task ExecuteWithRetryAsync_NullFactory_ThrowsArgumentNullException()
    {
        using var client = CreateClient();
        await Assert.ThrowsAsync<ArgumentNullException>(() => client.ExecuteWithRetryAsync(null!, "op", CancellationToken.None));
    }

    [Fact]
    public async Task ExecuteWithRetryAsync_RecordsHttpMetrics()
    {
        var metrics = new MetricsRegistry();
        using var client = new TestableClient(metrics, maxAttempts: 1);

        using var response = await client.ExecuteWithRetryAsync(
            () => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)),
            "test-op",
            CancellationToken.None);

        var rendered = metrics.RenderPrometheus(new FeatureHealthTracker());
        Assert.Contains("wk7bot_http_requests_total", rendered);
        Assert.Contains("Test:test-op", rendered);
    }

    [Fact]
    public void Dispose_WithOwnedThrottle_DisposesWithoutError()
    {
        var client = new TestableClient(new MetricsRegistry(), disposeThrottle: true);
        client.Dispose();
    }

    [Fact]
    public void Dispose_WithSharedThrottle_DoesNotDisposeSharedSemaphore()
    {
        var shared = new SemaphoreSlim(1, 1);
        var client = new TestableClient(new MetricsRegistry(), disposeThrottle: false);
        client.Dispose();

        // If the shared semaphore had been disposed, this would throw ObjectDisposedException.
        Assert.Equal(1, shared.CurrentCount);
        shared.Dispose();
    }
}
