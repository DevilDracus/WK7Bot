namespace WK7Bot.Services;

using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Options;
using WK7Bot.Core.Utilities;
using WK7Bot.Models;
using WK7Bot.Options;

/// <summary>
/// Background service that drains the error notification queue and delivers every buffered report
/// as a rich embed direct message to each configured recipient once the Discord gateway is connected.
/// Reports are delivered in small batches with a short pause between messages to stay clear of
/// Discord rate limits; delivery problems are logged as warnings only so the dispatcher never
/// feeds the queue it is draining.
/// </summary>
public class ErrorNotificationDispatcher : BackgroundService
{
    /// <summary>
    /// Maximum number of reports delivered per polling cycle.
    /// </summary>
    public const int MaxBatchSize = 5;

    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan SendDelay = TimeSpan.FromSeconds(1);

    private readonly DiscordSocketClient _discordClient;
    private readonly ErrorNotificationQueue _queue;
    private readonly Wk7BotOptions _options;
    private readonly ILogger<ErrorNotificationDispatcher> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ErrorNotificationDispatcher"/> class.
    /// </summary>
    /// <param name="discordClient">The connected Discord socket client instance.</param>
    /// <param name="queue">The queue that buffers captured error reports.</param>
    /// <param name="options">The strongly-typed application configuration options.</param>
    /// <param name="logger">The diagnostic logging service.</param>
    public ErrorNotificationDispatcher(
        DiscordSocketClient discordClient,
        ErrorNotificationQueue queue,
        IOptions<Wk7BotOptions> options,
        ILogger<ErrorNotificationDispatcher> logger)
    {
        _discordClient = discordClient ?? throw new ArgumentNullException(nameof(discordClient));
        _queue = queue ?? throw new ArgumentNullException(nameof(queue));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    }

    /// <summary>
    /// Runs the delivery loop, waiting for the Discord gateway and forwarding buffered reports as direct messages.
    /// </summary>
    /// <param name="stoppingToken">Cancellation token monitored for background service shutdown.</param>
    /// <returns>A task representing the background execution process.</returns>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Features.ErrorNotificationsEnabled)
        {
            _logger.LogInformation("Error notification dispatcher is disabled via feature options.");
            _queue.Clear();
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (_discordClient.ConnectionState == ConnectionState.Connected)
                {
                    var delivered = 0;
                    while (delivered < MaxBatchSize && _queue.TryDequeue(out var notification))
                    {
                        await DeliverAsync(notification, stoppingToken);

                        delivered++;
                        if (delivered < MaxBatchSize)
                        {
                            await Task.Delay(SendDelay, stoppingToken);
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Unexpected failure while dispatching error notifications.");
            }

            await Task.Delay(PollInterval, stoppingToken);
        }
    }

    /// <summary>
    /// Renders the report once and sends it to every configured notification recipient.
    /// </summary>
    /// <param name="notification">The error report to deliver.</param>
    /// <param name="stoppingToken">Cancellation token monitored for background service shutdown.</param>
    /// <returns>A task representing the delivery operation.</returns>
    private async Task DeliverAsync(ErrorNotification notification, CancellationToken stoppingToken)
    {
        if (_options.ErrorNotifyUserIds.Count == 0)
        {
            return;
        }

        var embed = ErrorEmbedBuilder.Build(notification);

        foreach (var idText in _options.ErrorNotifyUserIds)
        {
            stoppingToken.ThrowIfCancellationRequested();

            if (!ulong.TryParse(idText, out var userId))
            {
                _logger.LogWarning("Skipping invalid error notification user ID '{UserId}'.", idText);
                continue;
            }

            try
            {
                IUser? user = _discordClient.GetUser(userId);
                if (user == null)
                {
                    user = await _discordClient.Rest.GetUserAsync(userId);
                }

                if (user == null)
                {
                    _logger.LogWarning("Could not resolve Discord user {UserId} for error notification.", userId);
                    continue;
                }

                var dmChannel = await user.CreateDMChannelAsync();
                await dmChannel.SendMessageAsync(embed: embed);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not deliver error notification DM to Discord user {UserId}.", userId);
            }
        }
    }
}
