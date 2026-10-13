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
        FeatureHealthTracker health,
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
                consecutiveFailures++;
                health.RecordFailure(featureKey, ex.Message);
                logger.LogError(
                    ex,
                    "Service {Feature} iteration failed ({ConsecutiveFailures}/{MaxFailures}).",
                    featureKey,
                    consecutiveFailures,
                    maxConsecutiveFailures);

                if (consecutiveFailures >= maxConsecutiveFailures)
                {
                    logger.LogError(
                        "Service {Feature} has failed {MaxFailures} times consecutively. Pausing for {RestartDelay} before resuming.",
                        featureKey,
                        maxConsecutiveFailures,
                        restartDelay.Value);

                    try
                    {
                        await Task.Delay(restartDelay.Value, stoppingToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }

                    consecutiveFailures = 0;
                    logger.LogInformation("Service {Feature} resuming after restart delay.", featureKey);
                    continue;
                }
            }

            try
            {
                await Task.Delay(interval, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
