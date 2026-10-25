namespace WK7Bot.Services;

using WK7Bot.Core.Utilities;
using WK7Bot.Options;
using WK7Bot.Services.Interfaces;

/// <summary>
/// Deletes Discord guild scheduled events after their end time so the guild event list stays clean.
/// </summary>
public class DiscordScheduledEventCleanupService : BackgroundService
{
    private readonly IDiscordScheduledEventService _scheduledEventService;
    private readonly IFeatureHealthTracker _health;
    private readonly ILogger<DiscordScheduledEventCleanupService> _logger;

    /// <summary>Initializes a new instance of the <see cref="DiscordScheduledEventCleanupService"/> class.</summary>
    public DiscordScheduledEventCleanupService(
        IDiscordScheduledEventService scheduledEventService,
        IFeatureHealthTracker health,
        ILogger<DiscordScheduledEventCleanupService> logger)
    {
        _scheduledEventService = scheduledEventService ?? throw new ArgumentNullException(nameof(scheduledEventService));
        _health = health ?? throw new ArgumentNullException(nameof(health));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(5));
        try
        {
            while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    var deleted = await _scheduledEventService.DeleteExpiredAsync(stoppingToken);
                    _health.RecordSuccess(FeatureKeys.DiscordEvents);
                    if (deleted > 0)
                    {
                        _logger.LogInformation("Deleted {Count} expired Discord scheduled event(s).", deleted);
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _health.RecordFailure(FeatureKeys.DiscordEvents, ex.Message);
                    _logger.LogError(ex, "Discord scheduled event cleanup failed.");
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Graceful shutdown.
        }
    }
}
