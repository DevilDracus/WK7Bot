namespace WK7Bot.Services;

using System.Globalization;
using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Options;
using WK7Bot.Core.Utilities;
using WK7Bot.Models;
using WK7Bot.Options;

/// <summary>
/// Background service that drains the error notification queue and delivers every buffered report
/// as a rich embed to the dedicated <c>#🤖WK7Errors❗</c> channel (created when missing, same pattern
/// as the other feature channels), pinging every configured recipient. Reports are delivered in small
/// batches with a short pause between messages to stay clear of Discord rate limits; delivery problems
/// are logged as warnings only so the dispatcher never feeds the queue it is draining. When no channel
/// can be resolved or created, the dispatcher falls back to direct messages.
/// </summary>
public class ErrorNotificationDispatcher : BackgroundService
{
    /// <summary>
    /// Maximum number of reports delivered per polling cycle.
    /// </summary>
    public const int MaxBatchSize = 5;

    /// <summary>
    /// Name of the channel the reports are posted to; created when missing.
    /// </summary>
    public const string TargetChannelName = "🤖WK7Errors❗";

    /// <summary>
    /// Topic of the auto-created error channel.
    /// </summary>
    public const string ChannelTopic = "Automatische Fehlerberichte des WK7-Bots (Error-/Critical-Logs und Abstürze).";

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
    /// Runs the delivery loop, waiting for the Discord gateway and forwarding buffered reports as
    /// embeds to the notification channel (or as direct messages when no channel is available).
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
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Unexpected failure while dispatching error notifications.");
            }

            try
            {
                await Task.Delay(PollInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // Shutdown: leaving the delay unguarded would let the OCE escape ExecuteAsync and
                // stop the whole host instead of just this service.
                break;
            }
        }
    }

    /// <summary>
    /// Renders the report once and posts it to the error notification channel, pinging every configured
    /// recipient. Direct messages are the fallback when no channel can be resolved or the post fails.
    /// </summary>
    /// <param name="notification">The error report to deliver.</param>
    /// <param name="stoppingToken">Cancellation token monitored for background service shutdown.</param>
    /// <returns>A task representing the delivery operation.</returns>
    private async Task DeliverAsync(ErrorNotification notification, CancellationToken stoppingToken)
    {
        var embed = ErrorEmbedBuilder.Build(notification);

        var channel = await ResolveErrorChannelAsync(stoppingToken);
        if (channel != null)
        {
            try
            {
                var ping = BuildChannelPing(_options.ErrorNotifyUserIds);
                await channel.SendMessageAsync(text: ping, embed: embed);
                return;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Stable message text so repeated failures collapse into one throttled report
                // instead of one per delivery attempt.
                _logger.LogWarning(ex, "Could not deliver error notification to the error channel; falling back to direct messages.");
            }
        }

        await DeliverAsDirectMessagesAsync(embed, stoppingToken);
    }

    /// <summary>
    /// Resolves the notification channel inside the target servers: the WK7 server (or the bot test
    /// server while <c>error_notifications</c> is listed in <c>servers.testing_features</c>), falling
    /// back to every guild the bot is in while no server ID is configured. The first guild that offers
    /// a resolvable channel wins; when the channel is missing it is created.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token monitored for background service shutdown.</param>
    /// <returns>The target text channel, or <see langword="null"/> when none could be resolved or created.</returns>
    private async Task<ITextChannel?> ResolveErrorChannelAsync(CancellationToken cancellationToken)
    {
        foreach (var guildId in GetTargetGuildIds())
        {
            var channel = await GetOrCreateErrorChannelAsync(guildId);
            if (channel != null)
            {
                return channel;
            }
        }

        return null;
    }

    /// <summary>
    /// Returns the IDs of the guilds that should receive error reports: the configured WK7 server (or
    /// the bot test server while <c>error_notifications</c> is listed in <c>servers.testing_features</c>),
    /// falling back to every guild while no server ID is configured. Virtual for testability.
    /// </summary>
    /// <returns>The target Discord guild IDs.</returns>
    protected virtual IReadOnlyList<ulong> GetTargetGuildIds()
        => AutomaticTargetResolver.Resolve(
            _options.Servers,
            FeatureKeys.RoutedFeatures.ErrorNotifications,
            _discordClient.Guilds.Select(g => g.Id),
            _logger);

    /// <summary>
    /// Resolves the error channel for a guild by ID, creating the read-only notification channel when missing.
    /// </summary>
    /// <param name="guildId">The target guild where channel existence is evaluated.</param>
    /// <returns>The text channel instance, or <see langword="null"/> when the guild cannot be resolved or the channel cannot be created.</returns>
    protected virtual Task<ITextChannel?> GetOrCreateErrorChannelAsync(ulong guildId)
    {
        var guild = _discordClient.GetGuild(guildId);
        return guild is null
            ? Task.FromResult<ITextChannel?>(null)
            : ChannelResolver.TryGetOrCreateFeatureChannelAsync(
                guild,
                _discordClient.CurrentUser.Id,
                TargetChannelName,
                ChannelTopic,
                _logger);
    }

    /// <summary>
    /// Delivers the rendered report as a direct message to every configured recipient.
    /// </summary>
    /// <param name="embed">The rendered error embed.</param>
    /// <param name="stoppingToken">Cancellation token monitored for background service shutdown.</param>
    /// <returns>A task representing the delivery operation.</returns>
    private async Task DeliverAsDirectMessagesAsync(Embed embed, CancellationToken stoppingToken)
    {
        if (_options.ErrorNotifyUserIds.Count == 0)
        {
            // Neither a usable channel nor recipients configured: reports are discarded silently.
            return;
        }

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

    /// <summary>
    /// Builds the mention prefix of a channel post: every configured notification user is pinged,
    /// so a report in the shared channel reaches the same people as the direct message would.
    /// Unparseable entries are skipped instead of being posted as a broken literal mention.
    /// </summary>
    /// <param name="userIds">The configured notification user IDs.</param>
    /// <returns>The mention text, or an empty string when none of the IDs is usable.</returns>
    internal static string BuildChannelPing(IReadOnlyCollection<string> userIds)
    {
        // NumberStyles.None rejects whitespace, signs and group separators, so a malformed config
        // entry cannot become a literal "<@…>" mention in the channel (the DM path is more lenient
        // by design, but a broken ping is visible to the whole channel).
        var mentions = userIds
            .Where(id => ulong.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out _))
            .Select(id => $"<@{id.Trim()}>")
            .ToList();

        return mentions.Count == 0 ? string.Empty : string.Join(' ', mentions);
    }
}
