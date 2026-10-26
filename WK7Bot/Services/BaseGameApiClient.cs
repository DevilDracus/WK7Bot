using System.Diagnostics;
using System.Net;
using System.Net.Http;
using Microsoft.Extensions.Logging;
using WK7Bot.Core.Utilities;

namespace WK7Bot.Services;

/// <summary>
/// Base class providing the shared HTTP transport behaviour used by game API clients, including concurrency
/// throttling, transient-failure retries with linear backoff, and transient-status classification.
/// </summary>
public abstract class BaseGameApiClient : IGameApiHttpTransport
{
    /// <summary>
    /// Gets the maximum number of attempts made before giving up on a transient failure.
    /// </summary>
    protected abstract int MaxAttempts { get; }

    /// <summary>
    /// Gets the base delay applied between retry attempts. Virtual for testability.
    /// </summary>
    protected virtual TimeSpan RetryDelay => TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// Gets the concurrency throttle controlling how many requests may be in flight simultaneously.
    /// </summary>
    protected abstract SemaphoreSlim ApiThrottle { get; }

    /// <summary>
    /// Gets the logger used for operational diagnostics.
    /// </summary>
    protected abstract ILogger Logger { get; }

    /// <summary>
    /// Gets the metrics registry used for HTTP instrumentation.
    /// </summary>
    protected MetricsRegistry Metrics { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="BaseGameApiClient"/> class.
    /// </summary>
    /// <param name="metrics">The metrics registry.</param>
    protected BaseGameApiClient(MetricsRegistry metrics)
    {
        Metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
    }

    /// <summary>
    /// Gets the service name used in log messages (e.g. "Steam", "Battle.net").
    /// </summary>
    protected abstract string ServiceName { get; }

    /// <inheritdoc/>
    public virtual async Task<HttpResponseMessage> SendThrottledAsync(
        Func<Task<HttpResponseMessage>> sendFactory,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sendFactory);

        await ApiThrottle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await sendFactory();
        }
        finally
        {
            ApiThrottle.Release();
        }
    }

    /// <inheritdoc/>
    public virtual async Task<HttpResponseMessage> ExecuteWithRetryAsync(
        Func<Task<HttpResponseMessage>> sendFactory,
        string operationName,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sendFactory);

        var stopwatch = Stopwatch.StartNew();
        var response = await HttpRetry.ExecuteAsync(
            () => SendThrottledAsync(sendFactory, cancellationToken),
            MaxAttempts,
            IsTransientFailure,
            attempt => TimeSpan.FromMilliseconds(RetryDelay.TotalMilliseconds * attempt),
            null,
            null,
            cancellationToken);

        if (IsTransientFailure(response))
        {
            Logger.LogWarning(
                "{ServiceName} API {Operation} still failing with status code {StatusCode} after {MaxAttempts} attempts.",
                ServiceName,
                operationName,
                response.StatusCode,
                MaxAttempts);
        }

        stopwatch.Stop();
        Metrics.RecordHttp(
            $"{ServiceName}:{operationName}",
            stopwatch.Elapsed.TotalMilliseconds,
            response.IsSuccessStatusCode);

        return response;
    }

    /// <inheritdoc/>
    public virtual bool IsTransientFailure(HttpResponseMessage response)
    {
        return response.StatusCode == HttpStatusCode.TooManyRequests
            || (int)response.StatusCode >= 500;
    }
}
