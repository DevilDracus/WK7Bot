namespace WK7Bot.Services;

using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using WK7Bot.Core.Interfaces;
using WK7Bot.Core.Utilities;
using WK7Bot.Options;

/// <summary>
/// Delivers due reminders: as a channel post that mentions the creator when the reminder was created
/// with <c>hier:true</c>, otherwise as a direct message. Delivered reminders are pruned after 30 days.
/// </summary>
public class ReminderBackgroundService : BackgroundService
{
    /// <summary>Delivery tick. Reminders are minute-granular, so 30 seconds is a safe cadence.</summary>
    private static readonly TimeSpan Tick = TimeSpan.FromSeconds(30);

    /// <summary>Retention for delivered reminders.</summary>
    private static readonly TimeSpan DeliveredRetention = TimeSpan.FromDays(30);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly DiscordSocketClient _discordClient;
    private readonly FeatureHealthTracker _health;
    private readonly Wk7BotOptions _options;
    private readonly ILogger<ReminderBackgroundService> _logger;

    /// <summary>Initializes a new instance of the <see cref="ReminderBackgroundService"/> class.</summary>
    /// <param name="scopeFactory">The scope factory used for the reminder repository.</param>
    /// <param name="discordClient">The connected Discord socket client.</param>
    /// <param name="health">The feature health tracker.</param>
    /// <param name="options">The strongly-typed bot options.</param>
    /// <param name="logger">The logger instance.</param>
    public ReminderBackgroundService(
        IServiceScopeFactory scopeFactory,
        DiscordSocketClient discordClient,
        FeatureHealthTracker health,
        IOptions<Wk7BotOptions> options,
        ILogger<ReminderBackgroundService> logger)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _discordClient = discordClient ?? throw new ArgumentNullException(nameof(discordClient));
        _health = health ?? throw new ArgumentNullException(nameof(health));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Features.RemindersEnabled)
        {
            _logger.LogInformation("Reminder background service is disabled via feature options.");
            return;
        }

        _logger.LogInformation("Reminder background service started.");

        using var timer = new PeriodicTimer(Tick);

        do
        {
            try
            {
                await DeliverDueAsync(stoppingToken);
                _health.RecordSuccess(FeatureKeys.Reminders);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _health.RecordFailure(FeatureKeys.Reminders, ex.Message);
                _logger.LogError(ex, "Delivering due reminders failed.");
            }
        }
        while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken));
    }

    /// <summary>
    /// Delivers every reminder whose due time has passed. A delivery failure (for example closed DMs)
    /// still marks the reminder delivered so it is not retried forever; the failure is logged.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The number of reminders processed.</returns>
    internal async Task<int> DeliverDueAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IReminderRepository>();

        var due = await repository.GetDueAsync(DateTime.UtcNow, cancellationToken);
        foreach (var reminder in due)
        {
            try
            {
                if (reminder.ChannelId > 0 && _discordClient.GetChannel(reminder.ChannelId) is IMessageChannel channel)
                {
                    await channel.SendMessageAsync($"⏰ <@{reminder.DiscordUserId}> Erinnerung: {reminder.Text}");
                }
                else
                {
                    var user = await _discordClient.GetUserAsync(reminder.DiscordUserId);
                    if (user is null)
                    {
                        _logger.LogWarning("Could not resolve Discord user {UserId} for reminder {ReminderId}.", reminder.DiscordUserId, reminder.Id);
                    }
                    else
                    {
                        await user.SendMessageAsync($"⏰ Erinnerung: {reminder.Text}");
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Could not deliver reminder {ReminderId}.", reminder.Id);
            }

            await repository.MarkDeliveredAsync(reminder.Id, cancellationToken);
        }

        await repository.PruneDeliveredAsync(DateTime.UtcNow - DeliveredRetention, cancellationToken);
        return due.Count;
    }
}
