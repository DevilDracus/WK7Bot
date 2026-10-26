namespace WK7Bot.Core.Utilities;

using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;

/// <summary>
/// Resilience policy for DWD warning feed requests. 5.1 migration: the retry-with-exponential-backoff
/// and the circuit breaker are expressed as one Polly <see cref="ResiliencePipeline{TResult}"/> applied
/// to a normalized <c>object</c> result, so every caller — whether it fetches an
/// <see cref="HttpResponseMessage"/>, a raw <c>string</c> or <c>byte[]</c> — is protected by the same
/// policy instead of a special-cased fast path.
/// </summary>
/// <remarks>
/// <para>
/// Cancellation is never treated as a failure: an <see cref="OperationCanceledException"/> (or a
/// <see cref="TaskCanceledException"/> whose token is genuinely cancelled) passes straight through and
/// neither advances the retry count nor trips the circuit breaker.
/// </para>
/// <para>
/// Transient HTTP status codes (5xx, 408 RequestTimeout) are detected on the result as well as on the
/// exception, so a Polly result-predicate can retry them without the caller inspecting the response.
/// </para>
/// <para>
/// The circuit breaker translates Polly's <see cref="BrokenCircuitException{TResult}"/> into
/// <see cref="DwdCircuitBreakerOpenException"/> so the existing DWD services and their tests keep
/// their original contract.
/// </para>
/// </remarks>
public sealed class DwdResiliencePolicy
{
    /// <summary>
    /// Maximum number of attempts per request (1 initial + 2 retries = 3 total).
    /// </summary>
    public const int MaxAttempts = 3;

    private static readonly TimeSpan BaseDelay = TimeSpan.FromSeconds(2);
    private const int CircuitBreakerThreshold = 5;
    private static readonly TimeSpan CircuitBreakerDuration = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Polly counts every individual attempt — including retries — towards the breaker's minimum
    /// throughput, while the legacy contract counted one logical failure per <see cref="ExecuteAsync{T}"/>
    /// call. Scaling the threshold by the attempt budget keeps "5 consecutive failed requests" meaning
    /// five failed requests rather than five failed attempts.
    /// </summary>
    private const int CircuitBreakerMinimumThroughput = CircuitBreakerThreshold * MaxAttempts;

    private readonly ILogger<DwdResiliencePolicy> _logger;
    private readonly ResiliencePipeline<object> _pipeline;

    /// <summary>
    /// Initializes a new instance of the <see cref="DwdResiliencePolicy"/> class.
    /// </summary>
    /// <param name="logger">The logger instance for resilience diagnostics.</param>
    public DwdResiliencePolicy(ILogger<DwdResiliencePolicy> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // One shared predicate: an outcome is "handled" (retryable / breaker-visible) when it is a
        // transient non-cancellation exception, or a result that represents a transient HTTP status.
        // Cancellation is deliberately excluded so shutdown never trips the breaker.
        static ValueTask<bool> ShouldHandle(RetryPredicateArguments<object> args)
            => new(IsHandledOutcome(args.Outcome));



        _pipeline = new ResiliencePipelineBuilder<object>()
            .AddRetry(new RetryStrategyOptions<object>
            {
                ShouldHandle = ShouldHandle,
                MaxRetryAttempts = MaxAttempts - 1,
                BackoffType = DelayBackoffType.Exponential,
                Delay = BaseDelay,
                UseJitter = false,
                OnRetry = args =>
                {
                    _logger.LogWarning(args.Outcome.Exception,
                        "DWD request attempt {Attempt} failed (status/degraded). Retrying after {Delay}ms.",
                        args.AttemptNumber + 1,
                        args.RetryDelay.TotalMilliseconds);
                    return ValueTask.CompletedTask;
                },
            })
            .AddCircuitBreaker(new CircuitBreakerStrategyOptions<object>
            {
                ShouldHandle = (CircuitBreakerPredicateArguments<object> args) => new ValueTask<bool>(IsHandledOutcome(args.Outcome)),
                FailureRatio = 1.0,
                SamplingDuration = TimeSpan.FromMinutes(1),
                MinimumThroughput = CircuitBreakerMinimumThroughput,
                BreakDuration = CircuitBreakerDuration,
                OnOpened = _ =>
                {
                    _logger.LogError(
                        "DWD circuit breaker opened after {Threshold} consecutive failures. Will retry at {RetryTime:O}.",
                        CircuitBreakerThreshold,
                        DateTime.UtcNow + CircuitBreakerDuration);
                    return ValueTask.CompletedTask;
                },
                OnClosed = _ =>
                {
                    _logger.LogInformation("DWD circuit breaker closed.");
                    return ValueTask.CompletedTask;
                },
            })
            .Build();
    }

    /// <summary>
    /// Executes a DWD request with retry and circuit breaker protection.
    /// </summary>
    /// <typeparam name="T">The response type.</typeparam>
    /// <param name="action">The HTTP operation to execute.</param>
    /// <param name="cancellationToken">Cancellation token for the overall operation.</param>
    /// <returns>The result of the action.</returns>
    /// <exception cref="DwdCircuitBreakerOpenException">The circuit breaker is open and the host is considered unreachable.</exception>
    public async Task<T> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> action,
        CancellationToken cancellationToken = default)
        where T : notnull
    {
        ArgumentNullException.ThrowIfNull(action);

        // Normalize every caller to object so the pipeline is built once. HttpResponseMessage is the
        // result-polish case; string/byte[] results are always opaque values and never "transient".
        // Polly refuses to start a pipeline whose token is already cancelled, so a pre-cancelled
        // request invokes its action exactly once — mirroring the legacy contract and its tests.
        if (cancellationToken.IsCancellationRequested)
        {
            try
            {
                return await action(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                cancellationToken.ThrowIfCancellationRequested();
            }
        }

        var adapted = new Func<CancellationToken, Task<object>>(async ct =>
            (object?)(await action(ct).ConfigureAwait(false)) ?? throw new InvalidOperationException("Action returned null."));

        try
        {
            var result = await _pipeline
                .ExecuteAsync<object>(_ => new ValueTask<object>(adapted(cancellationToken)), cancellationToken)
                .ConfigureAwait(false);
            return (T)result;
        }
        catch (Exception ex) when (ex is BrokenCircuitException<object> or BrokenCircuitException)
        {
            // Keep the historical contract for the DWD services (and their tests) intact.
            throw new DwdCircuitBreakerOpenException(
                $"DWD circuit breaker is open (will retry at {DateTime.UtcNow + CircuitBreakerDuration:O}). Skipping request.");
        }
    }

    /// <summary>
    /// Determines whether an outcome should advance the retry count and the circuit breaker.
    /// </summary>
    private static bool IsHandledOutcome(Outcome<object> outcome)
    {
        if (outcome.Exception is TaskCanceledException taskCanceled)
        {
            // Genuine cancellation propagates; a TaskCanceledException with no cancelled token is a timeout.
            return !taskCanceled.CancellationToken.IsCancellationRequested;
        }

        // Genuine (non-Task) cancellation propagates untouched.
        if (outcome.Exception is OperationCanceledException)
        {
            return false;
        }

        if (outcome.Exception is HttpRequestException { StatusCode: { } code })
        {
            return IsTransientStatusCode(code);
        }

        if (outcome.Exception is HttpRequestException or IOException or TimeoutException)
        {
            return true;
        }

        return IsTransientResult(outcome.Result);
    }

    /// <summary>
    /// Treats responses that describe a transient HTTP status as handled, so Polly retries/punishes them
    /// without the caller inspecting the status code itself.
    /// </summary>
    private static bool IsTransientResult(object? result)
    {
        switch (result)
        {
            case HttpResponseMessage response:
                return IsTransientStatusCode(response.StatusCode);
            default:
                return false;
        }
    }

    private static bool IsTransientStatusCode(HttpStatusCode statusCode)
        => (int)statusCode >= 500 || statusCode == HttpStatusCode.RequestTimeout;
}

/// <summary>
/// Exception thrown when the DWD circuit breaker is open and the request is skipped. Preserved as the
/// public contract of <see cref="DwdResiliencePolicy"/> after the Polly migration.
/// </summary>
public sealed class DwdCircuitBreakerOpenException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DwdCircuitBreakerOpenException"/> class.
    /// </summary>
    /// <param name="message">The exception message.</param>
    public DwdCircuitBreakerOpenException(string message)
        : base(message)
    {
    }
}
