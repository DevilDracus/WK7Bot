namespace WK7Bot.Services;

using Microsoft.Extensions.Options;
using WK7Bot.Core.Utilities;
using WK7Bot.Options;
using WK7Bot.Services.Interfaces;

/// <summary>
/// Periodically evaluates the per-user deal watchlists and sends DM alerts.
/// </summary>
public class DealWatchlistBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IFeatureHealthTracker _health;
    private readonly FeatureGate _featureGate;
    private readonly Wk7BotOptions _options;
    private readonly ILogger<DealWatchlistBackgroundService> _logger;

    /// <summary>Initializes a new instance of the <see cref="DealWatchlistBackgroundService"/> class.</summary>
    public DealWatchlistBackgroundService(
        IServiceScopeFactory scopeFactory,
        IFeatureHealthTracker health,
        FeatureGate featureGate,
        IOptions<Wk7BotOptions> options,
        ILogger<DealWatchlistBackgroundService> logger)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _health = health ?? throw new ArgumentNullException(nameof(health));
        _featureGate = featureGate ?? throw new ArgumentNullException(nameof(featureGate));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_featureGate.IsEnabled(FeatureKeys.DealWatchlist))
        {
            _logger.LogInformation("Deal watchlist is disabled via feature options.");
            return;
        }

        var interval = TimeSpan.FromHours(Math.Max(1, _options.DealWatchlistCheckIntervalHours));
        await BackgroundServiceResilience.RunWithResilienceAsync(
            async token =>
            {
                using var scope = _scopeFactory.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<IDealWatchlistService>();
                var alerts = await service.CheckAllAsync(token);
                _logger.LogInformation("Deal watchlist check completed; {Count} alert(s) sent.", alerts);
            },
            FeatureKeys.DealWatchlist,
            _health,
            _logger,
            interval,
            maxConsecutiveFailures: BackgroundServiceResilience.DefaultMaxConsecutiveFailures,
            restartDelay: BackgroundServiceResilience.DefaultRestartDelay,
            stoppingToken);
    }
}
