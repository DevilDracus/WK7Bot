namespace WK7Bot.Core.Utilities;

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

/// <summary>
/// Resilience policy for DWD warning feed requests. Combines retry-with-exponential-backoff
/// and a circuit breaker to handle transient TLS failures, timeouts, and the intermittent
/// 503/EOF errors observed from <c>opendata.dwd.de</c>.
/// </summary>
/// <remarks>
/// The DWD feed is a public government endpoint with no SLA. Transient failures are common:
/// TLS handshake resets, connection timeouts, and occasional 503 responses. Without retry
/// logic, a single transient failure kills the entire poll cycle and the service appears dead.
/// The circuit breaker prevents the bot from spamming an already-unreachable host.
/// </remarks>
public sealed class DwdResiliencePolicy
{
    private readonly ILogger _logger;
    private readonly object _gate = new();

    private int _consecutiveFailures;
    private DateTime _circuitOpenedAtUtc;
    private bool _circuitOpen;

    /// <summary>
    /// Maximum number of retry attempts per request (1 initial + 2 retries = 3 total).
    /// </summary>
    public const int MaxAttempts = 3;

    /// <summary>
    /// Base delay for exponential backoff. Attempt 1 waits 2s, attempt 2 waits 4s.
    /// </summary>
    private static readonly TimeSpan BaseDelay = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Number of consecutive failures before the circuit breaker opens.
    /// </summary>
    private const int CircuitBreakerThreshold = 5;

    /// <summary>
    /// How long the circuit breaker stays open before allowing a trial request.
    /// </summary>
    private static readonly TimeSpan CircuitBreakerDuration = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Initializes a new instance of the <see cref="DwdResiliencePolicy"/> class.
    /// </summary>
    /// <param name="logger">The logger instance for resilience diagnostics.</param>
    public DwdResiliencePolicy(ILogger logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Executes a DWD request with retry and circuit breaker protection.
    /// </summary>
    /// <typeparam name="T">The response type.</typeparam>
    /// <param name="action">The HTTP operation to execute.</param>
    /// <param name="cancellationToken">Cancellation token for the overall operation.</param>
    /// <returns>The result of the action.</returns>
    /// <exception cref="DwdCircuitBreakerOpenException">The circuit breaker is open and the host is considered unreachable.</exception>
    /// <exception cref="HttpRequestException">All retry attempts were exhausted.</exception>
    public async Task<T> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> action,
        CancellationToken cancellationToken = default)
    {
        if (IsCircuitOpen())
        {
            throw new DwdCircuitBreakerOpenException(
                $"DWD circuit breaker is open (opened at {_circuitOpenedAtUtc:O}). Skipping request.");
        }

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            try
            {
                var result = await action(cancellationToken).ConfigureAwait(false);
                RecordSuccess();
                return result;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (IsTransient(ex) && attempt < MaxAttempts)
            {
                var delay = TimeSpan.FromTicks(BaseDelay.Ticks * (1 << (attempt - 1)));
                _logger.LogWarning(
                    ex,
                    "DWD request attempt {Attempt}/{MaxAttempts} failed: {Message}. Retrying in {DelayMs}ms.",
                    attempt,
                    MaxAttempts,
                    ex.Message,
                    delay.TotalMilliseconds);

                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (IsTransient(ex))
            {
                RecordFailure();
                _logger.LogError(
                    ex,
                    "DWD request failed after {MaxAttempts} attempts: {Message}",
                    MaxAttempts,
                    ex.Message);
                throw;
            }
        }

        // Unreachable: the loop either returns or throws.
        throw new InvalidOperationException("DwdResiliencePolicy.ExecuteAsync fell through the retry loop.");
    }

    /// <summary>
    /// Determines whether an exception is transient and worth retrying.
    /// </summary>
    /// <param name="ex">The exception to inspect.</param>
    /// <returns><see langword="true"/> when the failure is likely transient.</returns>
    private static bool IsTransient(Exception ex)
    {
        return ex is HttpRequestException
            or TimeoutException
            or TaskCanceledException
            or IOException
            or DwdCircuitBreakerOpenException;
    }

    /// <summary>
    /// Records a successful request, resetting the circuit breaker.
    /// </summary>
    private void RecordSuccess()
    {
        lock (_gate)
        {
            _consecutiveFailures = 0;
            _circuitOpen = false;
        }
    }

    /// <summary>
    /// Records a failed request, opening the circuit breaker if the threshold is reached.
    /// </summary>
    private void RecordFailure()
    {
        lock (_gate)
        {
            _consecutiveFailures++;
            if (_consecutiveFailures >= CircuitBreakerThreshold && !_circuitOpen)
            {
                _circuitOpen = true;
                _circuitOpenedAtUtc = DateTime.UtcNow;
                _logger.LogError(
                    "DWD circuit breaker opened after {Threshold} consecutive failures. Will retry at {RetryTime:O}.",
                    CircuitBreakerThreshold,
                    _circuitOpenedAtUtc + CircuitBreakerDuration);
            }
        }
    }

    /// <summary>
    /// Checks whether the circuit breaker is currently open.
    /// </summary>
    /// <returns><see langword="true"/> when requests should be skipped.</returns>
    private bool IsCircuitOpen()
    {
        lock (_gate)
        {
            if (!_circuitOpen)
            {
                return false;
            }

            if (DateTime.UtcNow - _circuitOpenedAtUtc >= CircuitBreakerDuration)
            {
                // Half-open: allow one trial request.
                _circuitOpen = false;
                _consecutiveFailures = 0;
                _logger.LogInformation("DWD circuit breaker half-open: allowing trial request.");
                return false;
            }

            return true;
        }
    }
}

/// <summary>
/// Exception thrown when the DWD circuit breaker is open and the request is skipped.
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
