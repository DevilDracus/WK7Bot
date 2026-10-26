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
    private readonly IFeatureHealthTracker _health;
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
        IFeatureHealthTracker health,
        IOptions<Wk7BotOptions> options,
        ILogger<ErrorNotificationDispatcher> logger)
    {
        _discordClient = discordClient ?? throw new ArgumentNullException(nameof(discordClient));
        _queue = queue ?? throw new ArgumentNullException(nameof(queue));
        _health = health ?? throw new ArgumentNullException(nameof(health));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
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

        await BackgroundServiceResilience.RunWithResilienceAsync(
            async ct =>
            {
                if (_discordClient.ConnectionState == ConnectionState.Connected)
                {
                    var delivered = 0;
                    while (delivered < MaxBatchSize && _queue.TryDequeue(out var notification))
                    {
                        await DeliverAsync(notification, ct).ConfigureAwait(false);
                        delivered++;
                        if (delivered < MaxBatchSize)
                        {
                            await Task.Delay(SendDelay, ct).ConfigureAwait(false);
                        }
                    }
                }

                await Task.Delay(PollInterval, ct).ConfigureAwait(false);
            },
            FeatureKeys.ErrorNotifications,
            _health,
            _logger,
            TimeSpan.Zero,
            stoppingToken: stoppingToken).ConfigureAwait(false);
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
        var plainTextMirror = BuildPlainTextMirror(notification);

        var channel = await ResolveErrorChannelAsync(stoppingToken);
        if (channel != null)
        {
            var embedSent = false;
            try
            {
                var ping = BuildChannelPing(_options.ErrorNotifyUserIds);
                await channel.SendMessageAsync(text: ping, embed: embed).ConfigureAwait(false);
                embedSent = true;

                try
                {
                    await channel.SendMessageAsync(text: plainTextMirror).ConfigureAwait(false);
                    return;
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    // The embed reached the channel but the mirror did not. Do not fall back to
                    // direct messages here, because that would duplicate the already-delivered report.
                    _logger.LogWarning(ex, "Error notification embed was delivered, but its plain-text mirror failed; continuing without DM fallback.");
                    return;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                if (embedSent)
                {
                    // Defensive only: the mirror-only path above already returns. Regardless, no
                    // DM duplication should occur after at least one channel delivery.
                    _logger.LogWarning(ex, "Error notification partial delivery failure; DM fallback skipped.");
                    return;
                }

                _logger.LogWarning(ex, "Could not deliver error notification embed to the error channel; falling back to direct messages.");
            }
        }

        await DeliverAsDirectMessagesAsync(embed, plainTextMirror, stoppingToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Resolves the error channel inside the target guilds: the test server only. The first guild
    /// that offers a resolvable channel wins; when the channel is missing it is created.
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
    /// Returns the IDs of the guilds that should receive error reports: always the bot test server —
    /// error reports must never be posted to the WK7 production server. When no test server is
    /// configured the list is empty and delivery falls back to direct messages. Virtual for testability.
    /// </summary>
    /// <returns>The target Discord guild IDs (empty when no test server is configured).</returns>
    protected virtual IReadOnlyList<ulong> GetTargetGuildIds()
        => AutomaticTargetResolver.ResolveTestServerOnly(_options.Servers, _logger);

    /// <summary>
    /// Resolves the error channel for a guild by ID, creating the read-only notification channel when missing.
    /// </summary>
    /// <param name="guildId">The target guild where channel existence is evaluated.</param>
    /// <returns>The text channel instance, or <see langword="null"/> when the guild cannot be resolved or the channel cannot be created.</returns>
    protected virtual Task<ITextChannel?> GetOrCreateErrorChannelAsync(ulong guildId)
    {
        var guild = _discordClient.GetGuild(guildId);
        // CurrentUser is only populated once the gateway has identified the bot; between the loop's
        // Connected check and this call the client can disconnect, leaving it null.
        var currentUser = _discordClient.CurrentUser;
        return guild is null || currentUser is null
            ? Task.FromResult<ITextChannel?>(null)
            : ChannelResolver.TryGetOrCreateFeatureChannelAsync(
                guild,
                currentUser.Id,
                TargetChannelName,
                ChannelTopic,
                _logger);
    }

    /// <summary>
    /// Delivers the rendered report as a direct message to every configured recipient.
    /// </summary>
    /// <param name="embed">The rendered error embed.</param>
    /// <param name="plainTextMirror">A plain-text summary of the error.</param>
    /// <param name="stoppingToken">Cancellation token monitored for background service shutdown.</param>
    /// <returns>A task representing the delivery operation.</returns>
    private async Task DeliverAsDirectMessagesAsync(Embed embed, string plainTextMirror, CancellationToken stoppingToken)
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
                await dmChannel.SendMessageAsync(embed: embed).ConfigureAwait(false);

                // Also send the plain-text mirror in DMs for consistency.
                await dmChannel.SendMessageAsync(text: plainTextMirror).ConfigureAwait(false);
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

    /// <summary>
    /// Builds a plain-text one-line summary of an error report for channels where embed bodies
    /// are not readable by external tools (e.g. the Discord API <c>fetch_messages</c> action).
    /// The summary contains the service name, exception type, a short message excerpt, and a
    /// stack-trace hash so operators can grep for recurring failures efficiently.
    /// </summary>
    /// <param name="notification">The error report to summarise.</param>
    /// <returns>A plain-text summary string, max 200 characters.</returns>
    internal static string BuildPlainTextMirror(ErrorNotification notification)
    {
        ArgumentNullException.ThrowIfNull(notification);

        var context = ContextHelpers.ShortContext(notification.Context);
        var exceptionType = notification.ExceptionType ?? "Error";
        var message = Truncate(notification.Message, 80);
        var hash = ComputeStackTraceHash(notification.StackTrace);

        var summary = $"⚠️ {context}: {exceptionType} [{hash}]";
        if (!string.IsNullOrWhiteSpace(message))
        {
            summary += $" — {message}";
        }

        return Truncate(summary, 200);
    }

    /// <summary>
    /// Computes a deterministic 8-character hex hash from the first 500 characters of a stack trace.
    /// Operators can use this to identify recurring failures without parsing the full stack trace.
    /// </summary>
    /// <param name="stackTrace">The full stack trace string, or <see langword="null"/>.</param>
    /// <returns>An 8-character hex hash, or <c>"none"</c> when the input is empty.</returns>
    private static string ComputeStackTraceHash(string? stackTrace)
    {
        if (string.IsNullOrWhiteSpace(stackTrace))
        {
            return "none";
        }

        var trimmed = stackTrace.Trim().Length > 500 ? stackTrace[..500] : stackTrace.Trim();
        var bytes = System.Text.Encoding.UTF8.GetBytes(trimmed);
        var hash = System.Security.Cryptography.MD5.HashData(bytes);
        return Convert.ToHexString(hash)[..8];
    }

    /// <summary>
    /// Truncates a string to the specified maximum length, appending an ellipsis when truncated.
    /// </summary>
    /// <param name="text">The text to truncate.</param>
    /// <param name="maxLength">The maximum length including the ellipsis.</param>
    /// <returns>The truncated text.</returns>
    private static string Truncate(string text, int maxLength)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxLength)
        {
            return text;
        }

        return text[..(maxLength - 3)] + "...";
    }
}
