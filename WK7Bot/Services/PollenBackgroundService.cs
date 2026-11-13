namespace WK7Bot.Services;

using System.Globalization;
using System.Text.Json;
using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using WK7Bot.Core.Entities;
using WK7Bot.Core.Interfaces;
using WK7Bot.Core.Utilities;
using WK7Bot.Models;
using WK7Bot.Options;
using WK7Bot.Services.Interfaces;

/// <summary>
/// Posts the DWD pollen and UV forecast at the configured local time to the weather channel.
/// Dispatch state is persisted per guild (including the last communicated forecast) so a restart
/// inside the same day never re-posts, posts happen at most every 3 days, and a DWD revision of
/// an already-communicated day triggers an earlier post.
/// </summary>
public class PollenBackgroundService : BackgroundService
{
    private static readonly JsonSerializerOptions ForecastJsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly Wk7BotJsonContext PollenJsonContext = new(ForecastJsonOptions);

    /// <summary>
    /// Minimum number of days between two pollen posts for the same guild. A post is due again once
    /// at least this many days have elapsed since the last one.
    /// </summary>
    private const int PollenCadenceDays = 3;

    /// <summary>
    /// Number of forecast days (today, tomorrow, day after tomorrow) covered by a single DWD pollen
    /// forecast and compared during change detection.
    /// </summary>
    private const int ForecastDayCount = 3;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly DiscordSocketClient _discordClient;
    private readonly IPollenService _pollenService;
    private readonly IFeatureHealthTracker _health;
    private readonly Wk7BotOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<PollenBackgroundService> _logger;

    /// <summary>Initializes a new instance of the <see cref="PollenBackgroundService"/> class.</summary>
    /// <param name="scopeFactory">The scope factory used for the dispatch state.</param>
    /// <param name="discordClient">The connected Discord socket client.</param>
    /// <param name="pollenService">The DWD pollen/UV service.</param>
    /// <param name="health">The feature health tracker.</param>
    /// <param name="options">The strongly-typed bot options.</param>
    /// <param name="logger">The logger instance.</param>
    /// <param name="timeProvider">Optional time source; defaults to the system clock.</param>
    public PollenBackgroundService(
        IServiceScopeFactory scopeFactory,
        DiscordSocketClient discordClient,
        IPollenService pollenService,
        IFeatureHealthTracker health,
        IOptions<Wk7BotOptions> options,
        ILogger<PollenBackgroundService> logger,
        TimeProvider? timeProvider = null)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _discordClient = discordClient ?? throw new ArgumentNullException(nameof(discordClient));
        _pollenService = pollenService ?? throw new ArgumentNullException(nameof(pollenService));
        _health = health ?? throw new ArgumentNullException(nameof(health));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Features.PollenAlertsEnabled)
        {
            _logger.LogInformation("Pollen background service is disabled via feature options.");
            return;
        }

        _logger.LogInformation("Starting pollen background service (daily at {Time}).", _options.PollenPostTime);

        await BackgroundServiceResilience.RunWithResilienceAsync(
            async token => await EvaluateScheduleAsync(_timeProvider.GetLocalNow().DateTime, token),
            FeatureKeys.PollenAlerts,
            _health,
            _logger,
            TimeSpan.FromMinutes(1),
            maxConsecutiveFailures: BackgroundServiceResilience.DefaultMaxConsecutiveFailures,
            restartDelay: BackgroundServiceResilience.DefaultRestartDelay,
            stoppingToken);
    }

    /// <summary>
    /// Dispatches the post once the configured local time has passed, applying the 3-day cadence
    /// and the DWD-revision exception.
    /// </summary>
    /// <param name="now">The current local time.</param>
    /// <param name="stoppingToken">Cancellation token.</param>
    /// <returns>A task representing the evaluation.</returns>
    internal async Task EvaluateScheduleAsync(DateTime now, CancellationToken stoppingToken)
    {
        if (!TryParseTimeOfDay(_options.PollenPostTime, out var postTime) || now.TimeOfDay < postTime)
        {
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var dispatchRepository = scope.ServiceProvider.GetRequiredService<IPollenDispatchRepository>();

        var guildIds = GetTargetGuildIds().ToList();

        if (guildIds.Count == 0)
        {
            return;
        }

        var pollen = await _pollenService.GetPollenAsync(stoppingToken);
        var uv = await _pollenService.GetUvAsync(stoppingToken);
        var embed = PollenEmbedBuilder.Build(pollen, uv);
        if (embed is null)
        {
            // Nothing to publish today; leave the dispatch open so the next tick retries.
            throw new InvalidOperationException("The DWD pollen and UV feeds were both unavailable.");
        }

        var forecastJson = pollen is null ? "null" : JsonSerializer.Serialize(pollen, PollenJsonContext.GetTypeInfo(typeof(PollenForecast))!);

        var lastByGuildId = await dispatchRepository.GetLastForGuildsAsync(guildIds, stoppingToken).ConfigureAwait(false);
        List<ulong> pending = [];
        foreach (var guildId in guildIds)
        {
            if (ShouldPost(lastByGuildId[guildId], now.Date, pollen))
            {
                pending.Add(guildId);
            }
        }

        if (pending.Count == 0)
        {
            return;
        }

        List<ulong> postedGuildIds = [];
        foreach (var guildId in pending)
        {
            var channel = await GetOrCreatePollenChannelAsync(guildId);
            if (channel is null)
            {
                _logger.LogWarning("Could not resolve or create the pollen channel for guild {GuildId}.", guildId);
                continue;
            }

            await PostToPollenChannelAsync(channel, embed, stoppingToken).ConfigureAwait(false);
            postedGuildIds.Add(guildId);
        }

        if (postedGuildIds.Count > 0)
        {
            await dispatchRepository.UpsertManyAsync(
                postedGuildIds.Select(guildId => (guildId, now.Date, forecastJson)),
                stoppingToken).ConfigureAwait(false);
        }
    }

    /// <summary>Resolves (or creates) the configured pollen channel in a guild.</summary>
    /// <param name="guildId">The target guild.</param>
    /// <returns>The channel, or <see langword="null"/>.</returns>
    protected virtual Task<ITextChannel?> GetOrCreatePollenChannelAsync(ulong guildId)
    {
        var guild = _discordClient.GetGuild(guildId);
        return guild is null
            ? Task.FromResult<ITextChannel?>(null)
            : ChannelResolver.TryGetOrCreateFeatureChannelAsync(
                guild,
                _discordClient.CurrentUser?.Id ?? 0,
                _options.PollenChannelName,
                "Pollenflug- und UV-Index-Vorhersage des DWD.",
                _logger);
    }

    /// <summary>Returns the IDs of the guilds that should receive pollen posts. Virtual for testability.</summary>
    /// <returns>The target Discord guild IDs.</returns>
    protected virtual IReadOnlyList<ulong> GetTargetGuildIds()
        => AutomaticTargetResolver.Resolve(
            _options.Servers,
            FeatureKeys.RoutedFeatures.PollenAlerts,
            _discordClient.Guilds.Select(guild => guild.Id),
            _logger);

    /// <summary>Posts the constructed embed to the resolved pollen channel. Virtual for testability.</summary>
    /// <param name="channel">The channel that receives the embed.</param>
    /// <param name="embed">The embed to publish.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes once the message has been sent.</returns>
    protected virtual Task PostToPollenChannelAsync(ITextChannel channel, Embed embed, CancellationToken cancellationToken)
        => CardSender.SendCardAsync(channel, CardBuilder.FromEmbed(embed), embed, _logger, cancellationToken);

    /// <summary>Parses an <c>HH:mm</c> time-of-day option.</summary>
    /// <param name="value">The configured value.</param>
    /// <param name="time">The parsed time of day.</param>
    /// <returns><see langword="true"/> when the value is a valid time.</returns>
    internal static bool TryParseTimeOfDay(string? value, out TimeSpan time)
        => TimeSpan.TryParseExact(value?.Trim(), new[] { @"h\:mm", @"hh\:mm" }, CultureInfo.InvariantCulture, out time)
           && time >= TimeSpan.Zero
           && time < TimeSpan.FromDays(1);

    /// <summary>
    /// Decides whether a post is due for <paramref name="today" /> given the last communicated state
    /// and the current forecast.
    /// </summary>
    /// <param name="last">The last communicated dispatch state, or <see langword="null" /> when none exists.</param>
    /// <param name="today">The current date.</param>
    /// <param name="current">The current pollen forecast, or <see langword="null" /> when unavailable.</param>
    /// <returns><see langword="true" /> when a post is due; otherwise <see langword="false" />.</returns>
    internal static bool ShouldPost(PollenDispatchState? last, DateTime today, PollenForecast? current)
    {
        if (last is null)
        {
            return true;
        }

        if (current is null)
        {
            return true;
        }

        var daysSince = (today.Date - last.LastPostedDate.Date).Days;
        if (daysSince < 0 || daysSince >= PollenCadenceDays)
        {
            return true;
        }

        PollenForecast? previous;
        try
        {
            previous = JsonSerializer.Deserialize(last.ForecastJson, typeof(PollenForecast), PollenJsonContext) as PollenForecast;
        }
        catch (JsonException)
        {
            return true;
        }

        if (previous is null)
        {
            return true;
        }

        return HasForecastChanged(last.LastPostedDate.Date, today.Date, previous, current);
    }

    /// <summary>
    /// Compares the previously communicated forecast with the current one and reports whether any
    /// overlapping day changed (level, type set) so a re-post is warranted.
    /// </summary>
    /// <param name="lastPostedDate">The date of the last post.</param>
    /// <param name="today">The current date.</param>
    /// <param name="previous">The previously communicated forecast.</param>
    /// <param name="current">The current forecast.</param>
    /// <returns><see langword="true" /> when a change was detected; otherwise <see langword="false" />.</returns>
    private static bool HasForecastChanged(DateTime lastPostedDate, DateTime today, PollenForecast previous, PollenForecast current)
    {
        for (var offset = 0; offset < ForecastDayCount; offset++)
        {
            var previousDate = lastPostedDate.Date.AddDays(offset);
            var currentOffset = (previousDate - today.Date).Days;
            if (currentOffset < 0 || currentOffset > ForecastDayCount - 1)
            {
                continue;
            }

            foreach (var currentType in current.Types)
            {
                var previousType = previous.Types.FirstOrDefault(type => type.Name == currentType.Name);
                if (previousType is null)
                {
                    return true;
                }

                var previousLevel = LevelAt(previousType, offset);
                var currentLevel = LevelAt(currentType, currentOffset);
                if (previousLevel is null || currentLevel is null)
                {
                    return true;
                }

                if (!string.Equals(previousLevel.Raw, currentLevel.Raw, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            foreach (var previousType in previous.Types)
            {
                if (!current.Types.Any(type => type.Name == previousType.Name))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static PollenLevel? LevelAt(PollenTypeForecast type, int offset) => offset switch
    {
        0 => type.Today,
        1 => type.Tomorrow,
        2 => type.DayAfterTomorrow,
        _ => null
    };
}
