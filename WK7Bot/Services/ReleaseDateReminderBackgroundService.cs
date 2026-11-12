namespace WK7Bot.Services;

using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Options;
using WK7Bot.Core.Entities;
using WK7Bot.Core.Interfaces;
using WK7Bot.Core.Utilities;
using WK7Bot.Options;
using WK7Bot.Services.Interfaces;

/// <summary>
/// Background service that checks tracked game release dates and notifies users when a release is approaching or has arrived.
/// </summary>
public class ReleaseDateReminderBackgroundService : BackgroundService
{
    private readonly DiscordSocketClient _discordClient;
    private readonly IReleaseDateReminderRepository _repository;
    private readonly ISteamStoreService _steamStoreService;
    private readonly IFeatureHealthTracker _health;
    private readonly FeatureGate _featureGate;
    private readonly Wk7BotOptions _options;
    private readonly ILogger<ReleaseDateReminderBackgroundService> _logger;

    /// <summary>
    /// How far in advance of the release date to send a notification.
    /// </summary>
    private static readonly TimeSpan AdvanceNotice = TimeSpan.FromDays(1);

    /// <summary>
    /// Cooldown between repeated notifications for the same game.
    /// </summary>
    private static readonly TimeSpan NotificationCooldown = TimeSpan.FromDays(7);

    /// <summary>How often the release-date reminder schedule is evaluated.</summary>
    private static readonly TimeSpan TickInterval = TimeSpan.FromHours(12);

    /// <summary>
    /// Initializes a new instance of the <see cref="ReleaseDateReminderBackgroundService"/> class.
    /// </summary>
    public ReleaseDateReminderBackgroundService(
        DiscordSocketClient discordClient,
        IReleaseDateReminderRepository repository,
        ISteamStoreService steamStoreService,
        IFeatureHealthTracker health,
        FeatureGate featureGate,
        IOptions<Wk7BotOptions> options,
        ILogger<ReleaseDateReminderBackgroundService> logger)
    {
        _discordClient = discordClient ?? throw new ArgumentNullException(nameof(discordClient));
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _steamStoreService = steamStoreService ?? throw new ArgumentNullException(nameof(steamStoreService));
        _health = health ?? throw new ArgumentNullException(nameof(health));
        _featureGate = featureGate ?? throw new ArgumentNullException(nameof(featureGate));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_featureGate.IsEnabled(FeatureKeys.ReleaseDateReminders))
        {
            _logger.LogInformation("Release date reminders are disabled via feature options.");
            return;
        }

        _logger.LogInformation("Starting Release Date Reminder Background Service.");

        await BackgroundServiceResilience.RunWithResilienceAsync(
            async token =>
            {
                await CheckReleasesAsync(token).ConfigureAwait(false);
            },
            FeatureKeys.ReleaseDateReminders,
            _health,
            _logger,
            TickInterval,
            maxConsecutiveFailures: BackgroundServiceResilience.DefaultMaxConsecutiveFailures,
            restartDelay: BackgroundServiceResilience.DefaultRestartDelay,
            stoppingToken);
    }

    /// <summary>
    /// Refreshes release dates for tracked games and sends notifications when thresholds are met.
    /// </summary>
    private async Task CheckReleasesAsync(CancellationToken cancellationToken)
    {
        var reminders = await _repository.GetAllAsync(cancellationToken).ConfigureAwait(false);
        if (reminders.Count == 0)
        {
            _logger.LogInformation("No release-date reminders configured.");
            return;
        }

        var now = DateTime.UtcNow;
        var notified = false;
        var updatedReminders = new List<ReleaseDateReminderItem>();

        foreach (var reminder in reminders)
        {
            try
            {
                var details = await _steamStoreService.GetAppDetailsAsync(reminder.AppId, cancellationToken).ConfigureAwait(false);
                if (details is null)
                {
                    _logger.LogWarning("Could not refresh release date for AppID {AppId}.", reminder.AppId);
                    continue;
                }

                reminder.Name = string.IsNullOrWhiteSpace(details.Name) ? reminder.Name : details.Name;
                reminder.IsTba = details.IsTba;
                reminder.ReleaseDate = details.ReleaseDate;
                updatedReminders.Add(reminder);

                if (details.IsTba)
                {
                    continue;
                }

                if (!details.ReleaseDate.HasValue)
                {
                    continue;
                }

                var releaseDateUtc = DateTime.SpecifyKind(details.ReleaseDate.Value.Date, DateTimeKind.Utc);
                var notifyWindowStart = releaseDateUtc - AdvanceNotice;
                var notifyWindowEnd = releaseDateUtc.AddDays(1);

                if (now < notifyWindowStart || now > notifyWindowEnd)
                {
                    continue;
                }

                if (reminder.LastNotifiedAt.HasValue && now - reminder.LastNotifiedAt.Value < NotificationCooldown)
                {
                    continue;
                }

                if (await NotifyUserAsync(reminder, releaseDateUtc, cancellationToken).ConfigureAwait(false))
                {
                    notified = true;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process release-date reminder for AppID {AppId}.", reminder.AppId);
            }
        }

        await _repository.UpdateManyAsync(updatedReminders, cancellationToken).ConfigureAwait(false);

        if (notified)
        {
            _logger.LogInformation("Sent release-date reminder notifications.");
        }
    }

    private async Task<bool> NotifyUserAsync(ReleaseDateReminderItem reminder, DateTime releaseDateUtc, CancellationToken cancellationToken)
    {
        var user = _discordClient.GetUser(reminder.DiscordUserId);
        if (user is null)
        {
            _logger.LogWarning("Could not resolve Discord user {UserId} for release reminder.", reminder.DiscordUserId);
            return false;
        }

        var daysUntil = (releaseDateUtc.Date - DateTime.UtcNow.Date).Days;
        var description = daysUntil switch
        {
            < 0 => $"**{reminder.Name}** ist jetzt verfügbar!",
            0 => $"**{reminder.Name}** erscheint heute!",
            1 => $"**{reminder.Name}** erscheint morgen!",
            _ => $"**{reminder.Name}** erscheint in {daysUntil} Tagen ({releaseDateUtc:dd.MM.yyyy})."
        };

        var embed = new EmbedBuilder()
            .WithTitle("🎮 Release-Erinnerung")
            .WithDescription(description)
            .WithUrl($"https://store.steampowered.com/app/{reminder.AppId}")
            .WithColor(Color.Teal)
            .WithTimestamp(DateTimeOffset.UtcNow)
            .WithFooter("WK7 Bot • Release-Datum")
            .Build();

        try
        {
            var dm = await user.CreateDMChannelAsync().ConfigureAwait(false);
            await dm.SendMessageAsync(embed: embed).ConfigureAwait(false);

            reminder.LastNotifiedAt = DateTime.UtcNow;
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not send release-date reminder to user {UserId}.", reminder.DiscordUserId);
            return false;
        }
    }
}
