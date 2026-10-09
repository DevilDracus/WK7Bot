using System.Globalization;
using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Options;
using WK7Bot.Core.Entities;
using WK7Bot.Core.Interfaces;
using WK7Bot.Core.Utilities;
using WK7Bot.Options;
using WK7Bot.Services.Interfaces;

namespace WK7Bot.Services;

/// <summary>
/// Background service that periodically checks for Leipzig waste collections,
/// posting daily confirmation notifications and weekly collection overviews to a dedicated Discord channel.
/// Dispatch state is persisted per guild and date so messages are never duplicated, even across bot restarts.
/// </summary>
public class LeipzigWasteBackgroundService : BackgroundService
{
    private const string TargetChannelName = "🗑️leipzig-waste";
    private readonly IServiceProvider _serviceProvider;
    private readonly DiscordSocketClient _discordClient;
    private readonly ILeipzigWasteService _wasteService;
    private readonly Wk7BotOptions _options;
    private readonly ILogger<LeipzigWasteBackgroundService> _logger;

    private DateTime _lastDailyNotificationDate = DateTime.MinValue;
    private DateTime _lastWeeklyOverviewDate = DateTime.MinValue;

    /// <summary>
    /// Initializes a new instance of the <see cref="LeipzigWasteBackgroundService"/> class.
    /// </summary>
    /// <param name="serviceProvider">The service provider used to create database scopes for dispatch state.</param>
    /// <param name="discordClient">The connected Discord socket client instance.</param>
    /// <param name="wasteService">The Leipzig waste schedule parser service.</param>
    /// <param name="options">The strongly-typed application configuration options.</param>
    /// <param name="logger">The logger instance for background execution diagnostics.</param>
    public LeipzigWasteBackgroundService(
        IServiceProvider serviceProvider,
        DiscordSocketClient discordClient,
        ILeipzigWasteService wasteService,
        IOptions<Wk7BotOptions> options,
        ILogger<LeipzigWasteBackgroundService> logger)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _discordClient = discordClient ?? throw new ArgumentNullException(nameof(discordClient));
        _wasteService = wasteService ?? throw new ArgumentNullException(nameof(wasteService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    }

    /// <summary>
    /// Runs the schedule loop, evaluating daily checks every morning at 08:00 AM and weekly overviews on Mondays at 09:00 AM.
    /// </summary>
    /// <param name="stoppingToken">Cancellation token monitored for background service shutdown.</param>
    /// <returns>A task representing the background execution process.</returns>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Features.LeipzigWasteEnabled)
        {
            _logger.LogInformation("Leipzig waste background service is disabled via feature options.");
            return;
        }

        _logger.LogInformation("Starting Leipzig Waste Background Service scheduler loop.");

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));

        while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await EvaluateScheduleAsync(DateTime.Now, stoppingToken);
            }
            catch (TaskCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while processing the Leipzig waste schedule evaluation loop.");
            }
        }
    }

    /// <summary>
    /// Evaluates the daily and weekly dispatch rules for the given point in time.
    /// In-memory flags only serve as a fast path; the persisted per-guild dispatch state is the
    /// authoritative duplicate guard, so restarting the bot does not re-send today's messages.
    /// </summary>
    /// <param name="now">The current local date and time.</param>
    /// <param name="stoppingToken">Cancellation token for network and database operations.</param>
    /// <returns>A task representing the asynchronous schedule evaluation.</returns>
    protected virtual async Task EvaluateScheduleAsync(DateTime now, CancellationToken stoppingToken)
    {
        if (now.Hour >= 8 && _lastDailyNotificationDate.Date < now.Date)
        {
            await CheckAndSendWasteNotificationsAsync(now.Date, stoppingToken);
            _lastDailyNotificationDate = now.Date;
        }

        if (now.DayOfWeek == DayOfWeek.Monday && now.Hour >= 9 && _lastWeeklyOverviewDate.Date < now.Date)
        {
            await SendWeeklyWasteOverviewAsync(now.Date, stoppingToken);
            _lastWeeklyOverviewDate = now.Date;
        }
    }

    /// <summary>
    /// Queries yesterday's waste collections and dispatches the confirmation embed to guilds that
    /// have not received it yet today.
    /// </summary>
    /// <param name="date">The calendar date the confirmation is dispatched for bookkeeping.</param>
    /// <param name="cancellationToken">Cancellation token for network operations.</param>
    /// <returns>A task representing the asynchronous notification process.</returns>
    private async Task CheckAndSendWasteNotificationsAsync(DateTime date, CancellationToken cancellationToken)
    {
        await DispatchAsync(WasteDispatchKinds.DailyConfirmation, date, async () =>
        {
            var yesterday = date.AddDays(-1);
            var collectionsYesterday = await _wasteService.GetWasteTypesForDateAsync(yesterday, cancellationToken);

            if (collectionsYesterday.Count == 0)
            {
                return null;
            }

            return BuildWasteNotificationEmbed(collectionsYesterday, yesterday);
        }, cancellationToken);
    }

    /// <summary>
    /// Builds the weekly overview embed and dispatches it to guilds that have not received this week's overview yet.
    /// </summary>
    /// <param name="monday">The Monday date identifying the target week.</param>
    /// <param name="cancellationToken">Cancellation token for network operations.</param>
    /// <returns>A task representing the asynchronous weekly overview process.</returns>
    private async Task SendWeeklyWasteOverviewAsync(DateTime monday, CancellationToken cancellationToken)
    {
        await DispatchAsync(WasteDispatchKinds.WeeklyOverview, monday,
            () => BuildWeeklyOverviewEmbedAsync(monday, cancellationToken),
            cancellationToken);
    }

    /// <summary>
    /// Filters target guilds through the persisted dispatch state, builds the embed lazily (only when a
    /// guild is still pending), posts it, and records success per guild so a partial failure retries
    /// only the guilds that did not receive the message.
    /// </summary>
    /// <param name="kind">The dispatch kind identifying the message type.</param>
    /// <param name="date">The calendar date used as the deduplication key.</param>
    /// <param name="embedFactory">Factory producing the embed, or <see langword="null"/> when nothing should be sent.</param>
    /// <param name="cancellationToken">Cancellation token for network and database operations.</param>
    /// <returns>A task representing the asynchronous dispatch process.</returns>
    private async Task DispatchAsync(string kind, DateTime date, Func<Task<Embed?>> embedFactory, CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var dispatchRepository = scope.ServiceProvider.GetRequiredService<IWasteDispatchRepository>();

        var pendingGuildIds = new List<ulong>();
        foreach (var guildId in GetTargetGuildIds())
        {
            if (!await dispatchRepository.HasSentAsync(kind, guildId, date, cancellationToken))
            {
                pendingGuildIds.Add(guildId);
            }
        }

        if (pendingGuildIds.Count == 0)
        {
            _logger.LogInformation(
                "Skipping {Kind} dispatch for {Date}: already sent to all {Count} guild(s) before a restart.",
                kind,
                date,
                GetTargetGuildIds().Count);
            return;
        }

        var embed = await embedFactory();
        if (embed == null)
        {
            return;
        }

        foreach (var guildId in pendingGuildIds)
        {
            var channel = await GetOrCreateWasteChannelAsync(guildId);
            if (channel == null)
            {
                _logger.LogWarning("Could not resolve or create the waste channel for guild {GuildId}; skipping.", guildId);
                continue;
            }

            await PostToWasteChannelAsync(channel, embed);

            // Record only after a successful send so a crash or failure retries this guild next tick
            // while guilds that already received the message stay suppressed.
            await dispatchRepository.MarkSentAsync(kind, guildId, date, cancellationToken);
        }

        _logger.LogDebug(
            "Dispatched {Kind} for {Date} to {Count} guild(s).",
            kind,
            date,
            pendingGuildIds.Count);
    }

    /// <summary>
    /// Returns the IDs of the guilds that should receive waste notifications: the configured WK7
    /// server (or the bot test server while <c>leipzig_waste</c> is listed in
    /// <c>servers.testing_features</c>), falling back to every guild while no server ID is
    /// configured. Virtual for testability.
    /// </summary>
    /// <returns>The target Discord guild IDs.</returns>
    protected virtual IReadOnlyList<ulong> GetTargetGuildIds()
        => AutomaticTargetResolver.Resolve(
            _options.Servers,
            "leipzig_waste",
            _discordClient.Guilds.Select(g => g.Id),
            _logger);

    /// <summary>
    /// Constructs a structured Discord Embed confirming the waste pickup from the previous day.
    /// </summary>
    /// <param name="wasteTypes">The list of waste collection types that were picked up yesterday.</param>
    /// <param name="date">The date on which the collection occurred.</param>
    /// <returns>The constructed embed ready for channel broadcast.</returns>
    private Embed BuildWasteNotificationEmbed(List<string> wasteTypes, DateTime date)
    {
        var formattedWasteList = string.Join("\n• ", wasteTypes);

        return new EmbedBuilder()
            .WithTitle("🗑️ Müllabholung Bestätigung")
            .WithDescription($"Gestern (**{date:dd.MM.yyyy}**) wurden folgende Tonnen abgeholt:\n\n• {formattedWasteList}\n\n Die Tonne(n) sollten nun leer sein!")
            .WithColor(Color.DarkGreen)
            .WithCurrentTimestamp()
            .Build();
    }

    /// <summary>
    /// Constructs a structured Discord Embed summarizing upcoming waste collections for the next 7 days in the style of the interaction module.
    /// </summary>
    /// <param name="startDate">The starting Monday date for the weekly overview evaluation.</param>
    /// <param name="cancellationToken">Cancellation token for calendar queries.</param>
    /// <returns>A task returning the constructed weekly overview embed.</returns>
    private async Task<Embed?> BuildWeeklyOverviewEmbedAsync(DateTime startDate, CancellationToken cancellationToken)
    {
        var germanCulture = new CultureInfo("de-DE");
        var endDate = startDate.AddDays(6);

        var embedBuilder = new EmbedBuilder()
            .WithTitle("🗑️ Stadtreinigung Leipzig — Wochenübersicht")
            .WithDescription($"Anstehende Müllabholungen für die Woche vom **{startDate:dd.MM.yyyy}** bis **{endDate:dd.MM.yyyy}**:")
            .WithColor(Color.Blue)
            .WithCurrentTimestamp();

        var foundAny = false;

        for (var i = 0; i < 7; i++)
        {
            var targetDate = startDate.AddDays(i);
            var collections = await _wasteService.GetWasteTypesForDateAsync(targetDate, cancellationToken);

            if (collections.Count == 0)
            {
                continue;
            }

            foundAny = true;

            var dayLabel = i switch
            {
                0 => "Heute (Montag)",
                _ => targetDate.ToString("dddd", germanCulture)
            };

            var formattedText = string.Join("\n• ", collections);

            embedBuilder.AddField($"{dayLabel} ({targetDate:dd.MM.yyyy})", $"• {formattedText}", inline: false);
        }

        if (!foundAny)
        {
            embedBuilder.WithDescription($"In der Woche vom **{startDate:dd.MM.yyyy}** bis **{endDate:dd.MM.yyyy}** stehen keine Müllabholungen an.");
        }

        return embedBuilder.Build();
    }

    /// <summary>
    /// Resolves the waste notification channel for a guild by ID, creating a new read-only channel when missing.
    /// </summary>
    /// <param name="guildId">The target guild where channel existence is evaluated.</param>
    /// <returns>The text channel instance, or <see langword="null"/> when the guild cannot be resolved.</returns>
    protected virtual async Task<ITextChannel?> GetOrCreateWasteChannelAsync(ulong guildId)
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
            properties.Topic = "Benachrichtigungen und Bestätigungen zur Stadtreinigung Leipzig Müllabholung.";
            properties.PermissionOverwrites = new List<Overwrite>
            {
                new(guild.EveryoneRole.Id, PermissionTarget.Role, new OverwritePermissions(
                    viewChannel: PermValue.Allow,
                    readMessageHistory: PermValue.Allow,
                    sendMessages: PermValue.Deny,
                    addReactions: PermValue.Allow
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
    /// Posts the constructed embed to the resolved waste channel. Virtual for testability.
    /// </summary>
    /// <param name="channel">The channel that receives the embed.</param>
    /// <param name="embed">The embed to publish.</param>
    /// <returns>A task that completes once the message has been sent.</returns>
    protected virtual Task PostToWasteChannelAsync(ITextChannel channel, Embed embed)
        => channel.SendMessageAsync(embed: embed);
}
