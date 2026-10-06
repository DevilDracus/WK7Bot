namespace WK7Bot.Services;

using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Options;
using WK7Bot.Core.Interfaces;
using WK7Bot.Core.Utilities;
using WK7Bot.Models;
using WK7Bot.Options;
using WK7Bot.Services.Interfaces;

/// <summary>
/// Background service that polls the DWD CAP warning feed every two minutes and posts a German
/// embed into the dedicated <c>#⛈️weather-warnings</c> channel when a configured event (heavy rain,
/// hail, storm, thunderstorm, …) is expected at the configured location within the lead window.
/// Dispatch is recorded per warning and guild so each warning is posted exactly once, even across
/// restarts; stale records are pruned after a week.
/// </summary>
public class DwdWarningBackgroundService : BackgroundService
{
    /// <summary>
    /// Name of the channel the warnings are posted to; created when missing.
    /// </summary>
    public const string TargetChannelName = "⛈️weather-warnings";

    /// <summary>
    /// How long posted dispatch records are kept before they are pruned.
    /// </summary>
    public const int PruneAgeDays = 7;

    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Grace period applied to the onset window so a warning whose onset slipped slightly into the
    /// past (feed latency) is still posted instead of silently dropped.
    /// </summary>
    private static readonly TimeSpan OnsetGrace = TimeSpan.FromMinutes(5);

    private readonly IServiceProvider _serviceProvider;
    private readonly DiscordSocketClient _discordClient;
    private readonly IDwdWarningService _warningSource;
    private readonly Wk7BotOptions _options;
    private readonly ILogger<DwdWarningBackgroundService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="DwdWarningBackgroundService"/> class.
    /// </summary>
    /// <param name="serviceProvider">The service provider used to create database scopes for dispatch state.</param>
    /// <param name="discordClient">The connected Discord socket client instance.</param>
    /// <param name="warningSource">The DWD warning feed client.</param>
    /// <param name="options">The strongly-typed application configuration options.</param>
    /// <param name="logger">The logger instance for background execution diagnostics.</param>
    public DwdWarningBackgroundService(
        IServiceProvider serviceProvider,
        DiscordSocketClient discordClient,
        IDwdWarningService warningSource,
        IOptions<Wk7BotOptions> options,
        ILogger<DwdWarningBackgroundService> logger)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _discordClient = discordClient ?? throw new ArgumentNullException(nameof(discordClient));
        _warningSource = warningSource ?? throw new ArgumentNullException(nameof(warningSource));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    }

    /// <summary>
    /// Runs the poll loop: the feed is checked once at startup and then every
    /// <see cref="PollInterval"/>, evaluating all warnings for the configured lead window.
    /// </summary>
    /// <param name="stoppingToken">Cancellation token monitored for background service shutdown.</param>
    /// <returns>A task representing the background execution process.</returns>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Features.DwdWarningEnabled)
        {
            _logger.LogInformation("DWD warning background service is disabled via feature options.");
            return;
        }

        if (!_options.DwdWarning.HasPostalCode())
        {
            _logger.LogWarning(
                "dwd_warning.postal_code is not set; DWD warning polling stays idle.");
            return;
        }

        if (!_options.DwdWarning.HasCoordinates())
        {
            _logger.LogWarning(
                "dwd_warning.latitude/longitude are not set for PLZ {PostalCode}; polygon matching is disabled and only area names are used.",
                _options.DwdWarning.PostalCode);
        }

        _logger.LogInformation(
            "Starting DWD warning poll loop for PLZ {PostalCode} (every {Interval}).",
            _options.DwdWarning.PostalCode,
            PollInterval);

        using var timer = new PeriodicTimer(PollInterval);

        do
        {
            try
            {
                await EvaluateAsync(DateTime.Now, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while evaluating DWD warnings.");
            }
        }
        while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken));
    }

    /// <summary>
    /// Fetches the newest warning snapshot and dispatches every warning that matches the configured
    /// events and location and whose onset lies within the lead window and expiry has not passed.
    /// </summary>
    /// <param name="now">The current local date and time.</param>
    /// <param name="stoppingToken">Cancellation token for network and database operations.</param>
    /// <returns>A task representing the asynchronous evaluation.</returns>
    protected virtual async Task EvaluateAsync(DateTime now, CancellationToken stoppingToken)
    {
        var configuration = _options.DwdWarning;
        var warnings = await _warningSource.FetchWarningsAsync(stoppingToken);

        foreach (var warning in warnings)
        {
            if (!IsRelevant(warning, configuration, now))
            {
                continue;
            }

            await DispatchWarningAsync(warning, now, stoppingToken);
        }
    }

    /// <summary>
    /// Determines whether a warning applies to the configured location, matches a configured event
    /// and is still inside the trigger window: its onset is at most <c>lead_minutes</c> ahead (with
    /// a small grace for feed latency) and its expiry has not passed.
    /// </summary>
    /// <param name="warning">The warning to test.</param>
    /// <param name="configuration">The postal-code-specific warning options.</param>
    /// <param name="now">The current local date and time.</param>
    /// <returns><see langword="true"/> when the warning should be posted.</returns>
    private static bool IsRelevant(CapWarning warning, DwdWarningOptions configuration, DateTime now)
    {
        if (now >= warning.Expires)
        {
            return false;
        }

        var untilOnset = warning.Onset - now;
        if (untilOnset < -OnsetGrace || untilOnset > TimeSpan.FromMinutes(configuration.LeadMinutes))
        {
            return false;
        }

        if (!CapWarningParser.MatchesEvent(warning, configuration.EffectiveEvents()))
        {
            return false;
        }

        return CapWarningParser.MatchesLocation(
            warning,
            configuration.Latitude,
            configuration.Longitude,
            configuration.AreaNames);
    }

    /// <summary>
    /// Posts a relevant warning to every guild that has not received it yet, recording success per
    /// guild so a partial failure retries only the guilds that did not receive the message. Stale
    /// dispatch records are pruned once at least one guild received the warning.
    /// </summary>
    /// <param name="warning">The warning to dispatch.</param>
    /// <param name="now">The current local time (anchors the embed countdown).</param>
    /// <param name="stoppingToken">Cancellation token for network and database operations.</param>
    /// <returns>A task representing the asynchronous dispatch process.</returns>
    private async Task DispatchWarningAsync(CapWarning warning, DateTime now, CancellationToken stoppingToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IWarningDispatchRepository>();

        var postedAny = false;

        foreach (var guildId in GetTargetGuildIds())
        {
            if (await repository.HasSentAsync(warning.Identifier, guildId, stoppingToken))
            {
                continue;
            }

            var channel = await GetOrCreateWarningChannelAsync(guildId);
            if (channel is null)
            {
                _logger.LogWarning(
                    "Could not resolve or create the warning channel for guild {GuildId}; skipping warning {WarningId}.",
                    guildId,
                    warning.Identifier);
                continue;
            }

            var embed = DwdWarningMessageBuilder.Build(warning, now, _options.DwdWarning);
            await PostToWarningChannelAsync(channel, embed);

            // Record only after a successful send so a crash or failure retries this guild on the
            // next poll while guilds that already received the warning stay suppressed.
            await repository.MarkSentAsync(warning.Identifier, guildId, now, stoppingToken);
            postedAny = true;

            _logger.LogDebug(
                "Posted DWD warning {Event} ({WarningId}) to guild {GuildId}.",
                warning.Event,
                warning.Identifier,
                guildId);
        }

        if (postedAny)
        {
            await repository.PruneAsync(now.AddDays(-PruneAgeDays), stoppingToken);
        }
    }

    /// <summary>
    /// Returns the IDs of all guilds that should receive warnings. Virtual for testability.
    /// </summary>
    /// <returns>The target Discord guild IDs.</returns>
    protected virtual IReadOnlyList<ulong> GetTargetGuildIds()
        => _discordClient.Guilds.Select(g => g.Id).ToList();

    /// <summary>
    /// Resolves the warning channel for a guild by ID, creating a new channel when missing.
    /// </summary>
    /// <param name="guildId">The target guild where channel existence is evaluated.</param>
    /// <returns>The text channel instance, or <see langword="null"/> when the guild cannot be resolved.</returns>
    protected virtual async Task<ITextChannel?> GetOrCreateWarningChannelAsync(ulong guildId)
    {
        var guild = _discordClient.GetGuild(guildId);
        if (guild == null)
        {
            return null;
        }

        var existingChannel = guild.TextChannels.FirstOrDefault(c => c.Name.Equals(TargetChannelName, StringComparison.OrdinalIgnoreCase));
        if (existingChannel != null)
        {
            return existingChannel;
        }

        return await guild.CreateTextChannelAsync(TargetChannelName, properties =>
        {
            properties.Topic = BuildChannelTopic();
            properties.PermissionOverwrites = new List<Overwrite>
            {
                new(guild.EveryoneRole.Id, PermissionTarget.Role, new OverwritePermissions(
                    viewChannel: PermValue.Allow,
                    readMessageHistory: PermValue.Allow,
                    sendMessages: PermValue.Deny
                )),
                new(_discordClient.CurrentUser.Id, PermissionTarget.User, new OverwritePermissions(
                    viewChannel: PermValue.Allow,
                    readMessageHistory: PermValue.Allow,
                    sendMessages: PermValue.Allow,
                    embedLinks: PermValue.Allow
                ))
            };
        });
    }

    /// <summary>
    /// Builds the channel topic from the configured postal code.
    /// </summary>
    /// <returns>The topic text.</returns>
    private string BuildChannelTopic()
        => _options.DwdWarning.HasPostalCode()
            ? $"Amtliche DWD-Warnungen für PLZ {_options.DwdWarning.PostalCode} – starker Regen, Hagel, Sturm und Gewitter."
            : "Amtliche DWD-Warnungen – starker Regen, Hagel, Sturm und Gewitter.";

    /// <summary>
    /// Posts the constructed warning embed to the resolved channel. Virtual for testability.
    /// </summary>
    /// <param name="channel">The channel that receives the warning.</param>
    /// <param name="embed">The warning embed.</param>
    /// <returns>A task that completes once the message has been sent.</returns>
    protected virtual Task PostToWarningChannelAsync(ITextChannel channel, Embed embed)
        => channel.SendMessageAsync(embed: embed);
}
