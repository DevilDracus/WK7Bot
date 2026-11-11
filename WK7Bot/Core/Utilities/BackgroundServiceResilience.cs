namespace WK7Bot.Core.Utilities;

/// <summary>
/// Resilience helper for <see cref="BackgroundService"/> loops. Wraps the per-iteration work with
/// consecutive-failure counting, automatic pause-and-restart, and uniform health recording.
/// </summary>
public static class BackgroundServiceResilience
{
    /// <summary>
    /// Default number of consecutive failures before the service pauses and retries after a delay.
    /// </summary>
    public const int DefaultMaxConsecutiveFailures = 3;

    /// <summary>
    /// Default pause duration after reaching the failure threshold.
    /// </summary>
    public static readonly TimeSpan DefaultRestartDelay = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Runs a background-service loop with automatic resilience: counts consecutive failures,
    /// records health, and pauses for <paramref name="restartDelay"/> after
    /// <paramref name="maxConsecutiveFailures"/> consecutive failures.
    /// </summary>
    /// <param name="iteration">The work to perform each loop tick.</param>
    /// <param name="featureKey">The feature key for health tracking.</param>
    /// <param name="health">The health tracker.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="interval">The delay between iterations.</param>
    /// <param name="maxConsecutiveFailures">Failures before pausing.</param>
    /// <param name="restartDelay">How long to pause after threshold failures.</param>
    /// <param name="stoppingToken">Cancellation token.</param>
    public static async Task RunWithResilienceAsync(
        Func<CancellationToken, Task> iteration,
        string featureKey,
        IFeatureHealthTracker health,
        ILogger logger,
        TimeSpan interval,
        int maxConsecutiveFailures = DefaultMaxConsecutiveFailures,
        TimeSpan? restartDelay = null,
        CancellationToken stoppingToken = default)
    {
        restartDelay ??= DefaultRestartDelay;
        int consecutiveFailures = 0;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await iteration(stoppingToken).ConfigureAwait(false);
                health.RecordSuccess(featureKey);
                consecutiveFailures = 0;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                var (handled, updatedFailures) = await HandleFailureAsync(
                    ex,
                    featureKey,
                    health,
                    logger,
                    maxConsecutiveFailures,
                    restartDelay.Value,
                    consecutiveFailures,
                    stoppingToken).ConfigureAwait(false);

                consecutiveFailures = updatedFailures;

                if (!handled)
                {
                    break;
                }

                continue;
            }

            if (!await TryDelayAsync(interval, stoppingToken).ConfigureAwait(false))
            {
                break;
            }
        }
    }

    private static async Task<(bool Handled, int ConsecutiveFailures)> HandleFailureAsync(
        Exception ex,
        string featureKey,
        IFeatureHealthTracker health,
        ILogger logger,
        int maxConsecutiveFailures,
        TimeSpan restartDelay,
        int consecutiveFailures,
        CancellationToken stoppingToken)
    {
        consecutiveFailures++;
        health.RecordFailure(featureKey, ex.Message);

        // External timeouts / network cancellations are transient environmental failures
        // (e.g. a container-to-host network stall) and are reported at Warning level so they
        // do not flood the Discord error channel. Genuine failures stay at Error level.
        var isTransientNetworkFailure = IsTransientNetworkFailure(ex);
        var failureLevel = isTransientNetworkFailure ? LogLevel.Warning : LogLevel.Error;

        logger.Log(
            failureLevel,
            ex,
            "Service {Feature} iteration failed ({ConsecutiveFailures}/{MaxFailures}).",
            featureKey,
            consecutiveFailures,
            maxConsecutiveFailures);

        if (consecutiveFailures < maxConsecutiveFailures)
        {
            return (true, consecutiveFailures);
        }

        logger.Log(
            failureLevel,
            "Service {Feature} has failed {MaxFailures} times consecutively. Pausing for {RestartDelay} before resuming.",
            featureKey,
            maxConsecutiveFailures,
            restartDelay);

        if (!await TryDelayAsync(restartDelay, stoppingToken).ConfigureAwait(false))
        {
            return (false, consecutiveFailures);
        }

        consecutiveFailures = 0;
        logger.LogInformation("Service {Feature} resuming after restart delay.", featureKey);
        return (true, consecutiveFailures);
    }

    private static async Task<bool> TryDelayAsync(TimeSpan interval, CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(interval, stoppingToken).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    /// <summary>
    /// Determines whether the given exception represents an external network timeout or cancellation
    /// (as opposed to a genuine, non-transient failure). Such failures are reported at Warning level
    /// so they do not flood the Discord error channel.
    /// </summary>
    /// <param name="ex">The exception thrown by the iteration.</param>
    /// <returns><see langword="true"/> when the exception is an external timeout or cancellation.</returns>
    private static bool IsTransientNetworkFailure(Exception ex)
    {
        // Genuine shutdown cancellation is re-thrown above and never reaches here, so any
        // TaskCanceledException here is an external timeout (e.g. HttpClient.Timeout).
        if (ex is TaskCanceledException or TimeoutException)
        {
            return true;
        }

        return ex.InnerException is TaskCanceledException or TimeoutException;
    }
}
