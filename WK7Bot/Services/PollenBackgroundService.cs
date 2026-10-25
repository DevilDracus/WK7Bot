namespace WK7Bot.Services;

using System.Globalization;
using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using WK7Bot.Core.Interfaces;
using WK7Bot.Core.Utilities;
using WK7Bot.Options;
using WK7Bot.Services.Interfaces;

/// <summary>
/// Posts the DWD pollen and UV forecast once a day at the configured local time to the weather
/// channel. Dispatch state is persisted per guild and date in the shared dispatch log, so a restart
/// inside the same day never re-posts.
/// </summary>
public class PollenBackgroundService : BackgroundService
{
    /// <summary>Dispatch kind recorded in the shared dispatch log.</summary>
    internal const string DispatchKind = "pollen";

    /// <summary>Retention of the dispatch records; mirrors the waste service.</summary>
    private static readonly TimeSpan DispatchRetention = TimeSpan.FromDays(90);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly DiscordSocketClient _discordClient;
    private readonly IPollenService _pollenService;
    private readonly IFeatureHealthTracker _health;
    private readonly Wk7BotOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<PollenBackgroundService> _logger;

    /// <summary>Initializes a new instance of the <see cref="PollenBackgroundService"/> class.</summary>
    /// <param name="scopeFactory">The scope factory used for the dispatch log.</param>
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
    /// Dispatches the daily post once the configured local time has passed.
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
        var dispatchRepository = scope.ServiceProvider.GetRequiredService<IWasteDispatchRepository>();

        await dispatchRepository.PruneAsync(now.Date - DispatchRetention, stoppingToken);

        var pending = new List<ulong>();
        foreach (var guildId in AutomaticTargetResolver.Resolve(
                     _options.Servers,
                     FeatureKeys.RoutedFeatures.PollenAlerts,
                     _discordClient.Guilds.Select(guild => guild.Id),
                     _logger))
        {
            if (!await dispatchRepository.HasSentAsync(DispatchKind, guildId, now.Date, stoppingToken))
            {
                pending.Add(guildId);
            }
        }

        if (pending.Count == 0)
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

        foreach (var guildId in pending)
        {
            var channel = await GetOrCreatePollenChannelAsync(guildId);
            if (channel is null)
            {
                _logger.LogWarning("Could not resolve or create the pollen channel for guild {GuildId}.", guildId);
                continue;
            }

            await CardSender.SendCardAsync(channel, CardBuilder.FromEmbed(embed), embed, _logger, stoppingToken);
            await dispatchRepository.MarkSentAsync(DispatchKind, guildId, now.Date, stoppingToken);
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
                _discordClient.CurrentUser.Id,
                _options.PollenChannelName,
                "Pollenflug- und UV-Index-Vorhersage des DWD.",
                _logger);
    }

    /// <summary>Parses an <c>HH:mm</c> time-of-day option.</summary>
    /// <param name="value">The configured value.</param>
    /// <param name="time">The parsed time of day.</param>
    /// <returns><see langword="true"/> when the value is a valid time.</returns>
    internal static bool TryParseTimeOfDay(string? value, out TimeSpan time)
        => TimeSpan.TryParseExact(value?.Trim(), new[] { @"h\:mm", @"hh\:mm" }, CultureInfo.InvariantCulture, out time)
           && time >= TimeSpan.Zero
           && time < TimeSpan.FromDays(1);
}
