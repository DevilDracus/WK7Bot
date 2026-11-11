namespace WK7Bot.Services;

using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Options;
using WK7Bot.Core.Utilities;
using WK7Bot.Models;
using WK7Bot.Options;
using WK7Bot.Services.Interfaces;

/// <summary>
/// Background service that posts a weekly digest of what the configured Steam-linked users are currently playing
/// and what they have played in the last two weeks.
/// </summary>
public class SteamFriendDigestBackgroundService : BackgroundService
{
    private readonly DiscordSocketClient _discordClient;
    private readonly ISteamService _steamService;
    private readonly IFeatureHealthTracker _health;
    private readonly FeatureGate _featureGate;
    private readonly Wk7BotOptions _options;
    private readonly ILogger<SteamFriendDigestBackgroundService> _logger;
    private DateOnly? _lastPostedWeek;

    /// <summary>Initializes a new instance of the <see cref="SteamFriendDigestBackgroundService"/> class.</summary>
    public SteamFriendDigestBackgroundService(
        DiscordSocketClient discordClient,
        ISteamService steamService,
        IFeatureHealthTracker health,
        FeatureGate featureGate,
        IOptions<Wk7BotOptions> options,
        ILogger<SteamFriendDigestBackgroundService> logger)
    {
        _discordClient = discordClient ?? throw new ArgumentNullException(nameof(discordClient));
        _steamService = steamService ?? throw new ArgumentNullException(nameof(steamService));
        _health = health ?? throw new ArgumentNullException(nameof(health));
        _featureGate = featureGate ?? throw new ArgumentNullException(nameof(featureGate));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_featureGate.IsEnabled(FeatureKeys.SteamFriendDigest))
        {
            _logger.LogInformation("Steam friend digest is disabled via feature options.");
            return;
        }

        if (string.IsNullOrWhiteSpace(_options.SteamApiKey))
        {
            _logger.LogWarning("Steam friend digest is enabled but no Steam API key is configured.");
            return;
        }

        var steamIds = _options.DiscordSteamMappings
            .Select(m => m.SteamId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToList();

        if (steamIds.Count == 0)
        {
            _logger.LogInformation("No Discord-Steam mappings configured; Steam friend digest will remain idle.");
            return;
        }

        _logger.LogInformation(
            "Starting Steam Friend Digest Background Service with {Count} mapped Steam ID(s).",
            steamIds.Count);

        await BackgroundServiceResilience.RunWithResilienceAsync(
            async ct =>
            {
                await MaybePostAsync(steamIds, ct).ConfigureAwait(false);
            },
            FeatureKeys.SteamFriendDigest,
            _health,
            _logger,
            TimeSpan.FromMinutes(1),
            stoppingToken: stoppingToken).ConfigureAwait(false);
    }

    /// <summary>Posts the digest when the configured weekday/time matches and it has not run this week.</summary>
    /// <param name="steamIds">The Steam IDs to include in the digest.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    protected virtual async Task MaybePostAsync(List<string> steamIds, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        if (!string.Equals(now.DayOfWeek.ToString(), _options.SteamFriendDigestPostWeekday, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (!TimeOnly.TryParse(_options.SteamFriendDigestPostTime, out var target) || now.TimeOfDay < target.ToTimeSpan())
        {
            return;
        }

        var daysSinceMonday = ((int)now.DayOfWeek + 6) % 7;
        var week = DateOnly.FromDateTime(now.AddDays(-daysSinceMonday));
        if (_lastPostedWeek == week)
        {
            return;
        }

        await BuildAndPostDigestAsync(steamIds, cancellationToken).ConfigureAwait(false);
        _lastPostedWeek = week;
    }

    /// <summary>
    /// Fetches Steam activity for the mapped users and posts the digest to the configured channel.
    /// </summary>
    internal async Task BuildAndPostDigestAsync(List<string> steamIds, CancellationToken cancellationToken)
    {
        var activities = await _steamService.GetUserActivitiesAsync(steamIds, cancellationToken).ConfigureAwait(false);
        if (activities.Count == 0)
        {
            _logger.LogWarning("No Steam activity data returned; skipping digest.");
            return;
        }

        var targetGuildIds = AutomaticTargetResolver.Resolve(
            _options.Servers,
            FeatureKeys.RoutedFeatures.SteamFriendDigest,
            _discordClient.Guilds.Select(g => g.Id),
            _logger);

        foreach (var guildId in targetGuildIds)
        {
            var guild = _discordClient.GetGuild(guildId);
            if (guild is null)
            {
                continue;
            }

            var channel = await ChannelResolver.TryGetOrCreateFeatureChannelAsync(
                guild,
                _discordClient.CurrentUser?.Id ?? 0,
                _options.SteamFriendDigestChannelName,
                "Wöchentlicher Überblick über Steam-Aktivitäten.",
                _logger);
            if (channel is null)
            {
                continue;
            }

            var embed = BuildDigestEmbed(activities);
            try
            {
                await channel.SendMessageAsync(embed: embed).ConfigureAwait(false);
                _logger.LogInformation("Posted Steam friend digest to {ChannelName}.", _options.SteamFriendDigestChannelName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to post Steam friend digest to {ChannelName}.", _options.SteamFriendDigestChannelName);
            }
        }
    }

    internal Embed BuildDigestEmbed(IReadOnlyList<SteamUserActivity> activities)
    {
        var builder = new EmbedBuilder()
            .WithTitle("🎮 Steam-Freunde Aktivität")
            .WithDescription("Hier ist, was die verknüpften Steam-Accounts gerade und in den letzten 14 Tagen gespielt haben.")
            .WithColor(Color.Blue)
            .WithTimestamp(DateTimeOffset.UtcNow)
            .WithFooter("WK7 Bot • Steam-Freunde Digest");

        var currentlyPlaying = activities
            .Where(a => !string.IsNullOrWhiteSpace(a.CurrentGameTitle))
            .ToList();

        if (currentlyPlaying.Count > 0)
        {
            var lines = currentlyPlaying
                .Select(a => $"\u2022 **{a.PersonaName}** spielt [{a.CurrentGameTitle}](https://store.steampowered.com/app/{a.CurrentGameAppId})")
                .ToList();
            builder.AddField("Gerade online", string.Join("\n", lines), inline: false);
        }

        var recentByUser = activities
            .Where(a => a.RecentGames.Count > 0)
            .OrderByDescending(a => a.RecentGames.Sum(g => g.PlaytimeTwoWeeksMinutes))
            .Take(10)
            .ToList();

        if (recentByUser.Count > 0)
        {
            foreach (var user in recentByUser)
            {
                var recentLines = user.RecentGames
                    .OrderByDescending(g => g.PlaytimeTwoWeeksMinutes)
                    .Take(3)
                    .Select(g => $"\u2022 {g.Name} \u2014 {SteamUserData.FormatPlaytime(g.PlaytimeTwoWeeksMinutes)}")
                    .ToList();

                var fieldTitle = $"{user.PersonaName} (14 Tage)";
                var fieldValue = string.Join("\n", recentLines);
                if (fieldValue.Length > 1024)
                {
                    fieldValue = fieldValue[..1021] + "...";
                }

                builder.AddField(fieldTitle, fieldValue, inline: true);
            }
        }
        else if (currentlyPlaying.Count == 0)
        {
            builder.AddField("Keine Aktivität", "In den letzten 14 Tagen wurde nichts auf Steam gespielt.", inline: false);
        }

        return builder.Build();
    }
}
