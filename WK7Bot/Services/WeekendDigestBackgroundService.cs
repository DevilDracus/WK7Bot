namespace WK7Bot.Services;

using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Options;
using WK7Bot.Core.Interfaces;
using WK7Bot.Core.Utilities;
using WK7Bot.Options;
using WK7Bot.Services.Interfaces;

/// <summary>
/// Background service that posts the Leipzig weekend digest: every Thursday at 14:15 it fetches the
/// upcoming weekend's events, curates three suggestions and posts them with a native Discord poll to
/// a dedicated channel. Dispatch state is persisted per guild and date so messages are never duplicated,
/// even across bot restarts.
/// </summary>
public class WeekendDigestBackgroundService : BackgroundService
{
    /// <summary>
    /// Name of the channel the digest is posted to; created when missing.
    /// </summary>
    private const string TargetChannelName = "📅wochenende";

    /// <summary>
    /// Dispatch kind used for restart-safe duplicate suppression.
    /// </summary>
    public const string DispatchKind = "weekend_digest";

    private static readonly TimeSpan DispatchTime = new(14, 15, 0);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMinutes(15);

    /// <summary>
    /// How often the Thursday dispatch is retried before the digest is skipped for the week.
    /// </summary>
    private const int MaxAttemptsPerDay = 4;

    private readonly IServiceProvider _serviceProvider;
    private readonly DiscordSocketClient _discordClient;
    private readonly IWeekendEventSource _eventSource;
    private readonly Wk7BotOptions _options;
    private readonly ILogger<WeekendDigestBackgroundService> _logger;

    private DateTime _attemptDay = DateTime.MinValue;
    private DateTime _nextAttemptAt = DateTime.MinValue;
    private int _attemptCount;
    private (Embed Embed, PollProperties? Poll)? _cachedDigest;

    /// <summary>
    /// Initializes a new instance of the <see cref="WeekendDigestBackgroundService"/> class.
    /// </summary>
    /// <param name="serviceProvider">The service provider used to create database scopes for dispatch state.</param>
    /// <param name="discordClient">The connected Discord socket client instance.</param>
    /// <param name="eventSource">The weekend event source providing the listing data.</param>
    /// <param name="options">The strongly-typed application configuration options.</param>
    /// <param name="logger">The logger instance for background execution diagnostics.</param>
    public WeekendDigestBackgroundService(
        IServiceProvider serviceProvider,
        DiscordSocketClient discordClient,
        IWeekendEventSource eventSource,
        IOptions<Wk7BotOptions> options,
        ILogger<WeekendDigestBackgroundService> logger)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _discordClient = discordClient ?? throw new ArgumentNullException(nameof(discordClient));
        _eventSource = eventSource ?? throw new ArgumentNullException(nameof(eventSource));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    }

    /// <summary>
    /// Runs the schedule loop, evaluating the Thursday dispatch window every minute.
    /// </summary>
    /// <param name="stoppingToken">Cancellation token monitored for background service shutdown.</param>
    /// <returns>A task representing the background execution process.</returns>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Features.WeekendDigestEnabled)
        {
            _logger.LogInformation("Weekend digest background service is disabled via feature options.");
            return;
        }

        _logger.LogInformation("Starting Weekend Digest Background Service scheduler loop.");

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));

        while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await EvaluateScheduleAsync(DateTime.Now, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while evaluating the weekend digest schedule.");
            }
        }
    }

    /// <summary>
    /// Evaluates the Thursday dispatch rule for the given point in time. The digest is attempted at
    /// 14:15 and retried at 15-minute intervals (at most <see cref="MaxAttemptsPerDay"/> times) while
    /// fetching or posting fails; a completed attempt ends the day. The persisted per-guild dispatch
    /// state is the authoritative duplicate guard, so restarting the bot does not re-send the digest.
    /// </summary>
    /// <param name="now">The current local date and time.</param>
    /// <param name="stoppingToken">Cancellation token for network and database operations.</param>
    /// <returns>A task representing the asynchronous schedule evaluation.</returns>
    protected virtual async Task EvaluateScheduleAsync(DateTime now, CancellationToken stoppingToken)
    {
        if (now.DayOfWeek != DayOfWeek.Thursday || now.TimeOfDay < DispatchTime)
        {
            return;
        }

        if (_attemptDay != now.Date)
        {
            _attemptDay = now.Date;
            _attemptCount = 0;
            _nextAttemptAt = DateTime.MinValue;
            _cachedDigest = null;
        }

        if (now < _nextAttemptAt)
        {
            return;
        }

        _attemptCount++;

        try
        {
            await DispatchDigestAsync(now.Date, now, stoppingToken);

            // A completed attempt (even one that skipped guilds without a channel) ends the day.
            _nextAttemptAt = DateTime.MaxValue;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Handled here (no rethrow) so one failed attempt produces exactly one log entry;
            // retries log as warnings and only the final give-up is an error. Shutdown
            // cancellation is not a failure and must not mutate the retry state.
            if (_attemptCount >= MaxAttemptsPerDay)
            {
                _logger.LogError(
                    ex,
                    "Giving up on the weekend digest for {Thursday:dd.MM.yyyy} after {Attempts} failed attempts.",
                    now.Date,
                    _attemptCount);
                _nextAttemptAt = DateTime.MaxValue;
            }
            else
            {
                _nextAttemptAt = now.Add(RetryDelay);
                _logger.LogWarning(
                    ex,
                    "Weekend digest attempt {Attempt}/{MaxAttempts} for {Thursday:dd.MM.yyyy} failed; retrying at {NextAttemptAt:HH:mm}.",
                    _attemptCount,
                    MaxAttemptsPerDay,
                    now.Date,
                    _nextAttemptAt);
            }
        }
    }

    /// <summary>
    /// Builds the digest once (reused across guilds and retries) and posts it to every guild that has
    /// not received this Thursday's digest yet, recording success per guild so a partial failure retries
    /// only the guilds that did not receive the message.
    /// </summary>
    /// <param name="thursday">The Thursday date identifying the dispatch.</param>
    /// <param name="now">The current local time, anchoring the poll duration.</param>
    /// <param name="cancellationToken">Cancellation token for network and database operations.</param>
    /// <returns>A task representing the asynchronous dispatch process.</returns>
    private async Task DispatchDigestAsync(DateTime thursday, DateTime now, CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var dispatchRepository = scope.ServiceProvider.GetRequiredService<IWasteDispatchRepository>();

        var targetGuildIds = GetTargetGuildIds();
        var pendingGuildIds = new List<ulong>();
        foreach (var guildId in targetGuildIds)
        {
            if (!await dispatchRepository.HasSentAsync(DispatchKind, guildId, thursday, cancellationToken))
            {
                pendingGuildIds.Add(guildId);
            }
        }

        if (pendingGuildIds.Count == 0)
        {
            _logger.LogInformation(
                "Skipping weekend digest dispatch for {Thursday:dd.MM.yyyy}: already sent to all {Count} guild(s) before a restart.",
                thursday,
                targetGuildIds.Count);
            return;
        }

        var digest = _cachedDigest ?? await BuildDigestAsync(thursday, now, cancellationToken);
        if (digest is null)
        {
            _logger.LogWarning(
                "No weekend events found for the weekend after {Thursday:dd.MM.yyyy}; marking the digest as handled.",
                thursday);

            foreach (var guildId in pendingGuildIds)
            {
                await dispatchRepository.MarkSentAsync(DispatchKind, guildId, thursday, cancellationToken);
            }

            return;
        }

        foreach (var guildId in pendingGuildIds)
        {
            var channel = await GetOrCreateWeekendChannelAsync(guildId);
            if (channel == null)
            {
                _logger.LogWarning("Could not resolve or create the weekend channel for guild {GuildId}; skipping.", guildId);
                continue;
            }

            await PostToWeekendChannelAsync(channel, digest.Value.Embed, digest.Value.Poll);

            // Record only after a successful send so a crash or failure retries this guild next attempt
            // while guilds that already received the digest stay suppressed.
            await dispatchRepository.MarkSentAsync(DispatchKind, guildId, thursday, cancellationToken);
        }

        _logger.LogDebug(
            "Dispatched weekend digest for {Thursday:dd.MM.yyyy} to {Count} guild(s).",
            thursday,
            pendingGuildIds.Count);
    }

    /// <summary>
    /// Fetches the upcoming weekend's events, curates the suggestions and builds the embed plus poll.
    /// The result is cached for the dispatch day so retries and additional guilds do not re-fetch.
    /// </summary>
    /// <param name="thursday">The Thursday date identifying the dispatch.</param>
    /// <param name="now">The current local time, anchoring the poll duration.</param>
    /// <param name="cancellationToken">Cancellation token for network operations.</param>
    /// <returns>The digest, or <see langword="null"/> when the weekend has no usable events.</returns>
    private async Task<(Embed Embed, PollProperties? Poll)?> BuildDigestAsync(
        DateTime thursday,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var weekendFriday = thursday.AddDays(1);
        var events = await _eventSource.FetchWeekendEventsAsync(weekendFriday, cancellationToken);

        var suggestions = WeekendDigestCurator.PickSuggestions(events, weekendFriday);
        if (suggestions.Count == 0)
        {
            return null;
        }

        var digest = WeekendDigestMessageBuilder.Build(suggestions, weekendFriday, now);
        _cachedDigest = digest;
        return digest;
    }

    /// <summary>
    /// Returns the IDs of the guilds that should receive the weekend digest: the configured WK7
    /// server (or the bot test server while <c>weekend_digest</c> is listed in
    /// <c>servers.testing_features</c>), falling back to every guild while no server ID is
    /// configured. Virtual for testability.
    /// </summary>
    /// <returns>The target Discord guild IDs.</returns>
    protected virtual IReadOnlyList<ulong> GetTargetGuildIds()
        => AutomaticTargetResolver.Resolve(
            _options.Servers,
            FeatureKeys.RoutedFeatures.WeekendDigest,
            _discordClient.Guilds.Select(g => g.Id),
            _logger);

    /// <summary>
    /// Resolves the weekend digest channel for a guild by ID, creating a new channel when missing.
    /// </summary>
    /// <param name="guildId">The target guild where channel existence is evaluated.</param>
    /// <returns>The text channel instance, or <see langword="null"/> when the guild cannot be resolved.</returns>
    protected virtual Task<ITextChannel?> GetOrCreateWeekendChannelAsync(ulong guildId)
    {
        var guild = _discordClient.GetGuild(guildId);
        return guild is null
            ? Task.FromResult<ITextChannel?>(null)
            : ChannelResolver.TryGetOrCreateFeatureChannelAsync(
                guild,
                _discordClient.CurrentUser.Id,
                TargetChannelName,
                "Wochenend-Tipps aus Leipzig – Märkte, Kultur und Ausflüge mit Abstimmung.",
                _logger,
                allowReactions: true);
    }

    /// <summary>
    /// Posts the constructed digest to the resolved weekend channel. Virtual for testability.
    /// </summary>
    /// <param name="channel">The channel that receives the digest.</param>
    /// <param name="embed">The digest embed.</param>
    /// <param name="poll">The poll to attach, or <see langword="null"/> when there is a single suggestion.</param>
    /// <returns>A task that completes once the message has been sent.</returns>
    protected virtual Task PostToWeekendChannelAsync(ITextChannel channel, Embed embed, PollProperties? poll)
        => poll is null
            ? channel.SendMessageAsync(embed: embed)
            : channel.SendMessageAsync(embed: embed, poll: poll);
}
