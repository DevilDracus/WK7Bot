namespace WK7Bot.Core.Utilities;

using System;
using Microsoft.Extensions.Logging;

/// <summary>
/// Circuit breaker for Gemini API calls. After a configurable number of consecutive failures,
/// the circuit opens for a cooldown period during which all Gemini requests are skipped
/// immediately. This prevents the food publisher from burning through retry budgets when
/// Gemini is unavailable.
/// </summary>
public sealed class GeminiCircuitBreaker
{
    private readonly ILogger<GeminiCircuitBreaker> _logger;
    private readonly Func<DateTime> _utcNow;
    private readonly object _gate = new();

    private int _consecutiveFailures;
    private DateTime _circuitOpenedAtUtc;
    private bool _circuitOpen;

    /// <summary>
    /// Number of consecutive failures before the circuit breaker opens.
    /// </summary>
    private const int CircuitBreakerThreshold = 3;

    /// <summary>
    /// How long the circuit breaker stays open before allowing a trial request.
    /// </summary>
    private static readonly TimeSpan CircuitBreakerDuration = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Initializes a new instance of the <see cref="GeminiCircuitBreaker"/> class.
    /// </summary>
    /// <param name="logger">The logger instance for circuit breaker diagnostics.</param>
    public GeminiCircuitBreaker(ILogger<GeminiCircuitBreaker> logger)
        : this(logger, () => DateTime.UtcNow)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="GeminiCircuitBreaker" /> class.
    /// </summary>
    /// <param name="logger">The logger instance for circuit breaker diagnostics.</param>
    /// <param name="utcNowProvider">Optional UTC-now provider; injectable for deterministic tests. Defaults to <see cref="DateTime.UtcNow" />.</param>
    public GeminiCircuitBreaker(ILogger<GeminiCircuitBreaker> logger, Func<DateTime>? utcNowProvider)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _utcNow = utcNowProvider ?? (() => DateTime.UtcNow);
    }

    /// <summary>
    /// Determines whether the circuit breaker is currently open and Gemini calls should be skipped.
    /// </summary>
    /// <returns><see langword="true"/> when the circuit is open; otherwise, <see langword="false"/>.</returns>
    public bool IsCircuitOpen()
    {
        lock (_gate)
        {
            if (!_circuitOpen)
            {
                return false;
            }

            if (_utcNow() - _circuitOpenedAtUtc >= CircuitBreakerDuration)
            {
                _circuitOpen = false;
                _consecutiveFailures = 0;
                _logger.LogInformation("Gemini circuit breaker half-open: allowing trial request.");
                return false;
            }

            return true;
        }
    }

    /// <summary>
    /// Records a successful Gemini call, resetting the failure counter.
    /// </summary>
    public void RecordSuccess()
    {
        lock (_gate)
        {
            _consecutiveFailures = 0;
            _circuitOpen = false;
        }
    }

    /// <summary>
    /// Records a failed Gemini call, opening the circuit breaker if the threshold is reached.
    /// </summary>
    public void RecordFailure()
    {
        lock (_gate)
        {
            _consecutiveFailures++;
            if (_consecutiveFailures >= CircuitBreakerThreshold && !_circuitOpen)
            {
                _circuitOpen = true;
                _circuitOpenedAtUtc = _utcNow();
                _logger.LogError(
                    "Gemini circuit breaker opened after {Threshold} consecutive failures. Will retry at {RetryTime:O}.",
                    CircuitBreakerThreshold,
                    _circuitOpenedAtUtc + CircuitBreakerDuration);
            }
        }
    }
}
