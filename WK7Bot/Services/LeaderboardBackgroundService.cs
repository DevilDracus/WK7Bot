namespace WK7Bot.Services;

using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Options;
using WK7Bot.Core.Utilities;
using WK7Bot.Options;
using WK7Bot.Services.Interfaces;

/// <summary>
/// Posts a weekly Steam playtime leaderboard to the configured channel.
/// </summary>
public class LeaderboardBackgroundService : BackgroundService
{
    private readonly DiscordSocketClient _discordClient;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IFeatureHealthTracker _health;
    private readonly FeatureGate _featureGate;
    private readonly Wk7BotOptions _options;
    private readonly ILogger<LeaderboardBackgroundService> _logger;
    private DateOnly? _lastPostedWeek;

    /// <summary>Initializes a new instance of the <see cref="LeaderboardBackgroundService"/> class.</summary>
    public LeaderboardBackgroundService(
        DiscordSocketClient discordClient,
        IServiceScopeFactory scopeFactory,
        IFeatureHealthTracker health,
        FeatureGate featureGate,
        IOptions<Wk7BotOptions> options,
        ILogger<LeaderboardBackgroundService> logger)
    {
        _discordClient = discordClient ?? throw new ArgumentNullException(nameof(discordClient));
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _health = health ?? throw new ArgumentNullException(nameof(health));
        _featureGate = featureGate ?? throw new ArgumentNullException(nameof(featureGate));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_featureGate.IsEnabled(FeatureKeys.Leaderboard))
        {
            _logger.LogInformation("Leaderboard is disabled via feature options.");
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        try
        {
            while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    await MaybePostAsync(stoppingToken).ConfigureAwait(false);
                    _health.RecordSuccess(FeatureKeys.Leaderboard);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _health.RecordFailure(FeatureKeys.Leaderboard, ex.Message);
                    _logger.LogError(ex, "Leaderboard weekly post failed.");
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Graceful shutdown.
        }
    }

    /// <summary>Posts the leaderboard when the configured weekday/time matches and it has not run this week.</summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    protected virtual async Task MaybePostAsync(CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        if (!string.Equals(now.DayOfWeek.ToString(), _options.LeaderboardPostWeekday, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (!TimeOnly.TryParse(_options.LeaderboardPostTime, out var target) || now.TimeOfDay < target.ToTimeSpan())
        {
            return;
        }

        var daysSinceMonday = ((int)now.DayOfWeek + 6) % 7;
        var week = DateOnly.FromDateTime(now.AddDays(-daysSinceMonday));
        if (_lastPostedWeek == week)
        {
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var leaderboardService = scope.ServiceProvider.GetRequiredService<ILeaderboardService>();
        var targetGuildIds = AutomaticTargetResolver.Resolve(
            _options.Servers,
            FeatureKeys.RoutedFeatures.Leaderboard,
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
                _options.LeaderboardChannelName,
                "Wöchentliche Spieler-Bestenlisten.",
                _logger);
            if (channel is null)
            {
                continue;
            }

            var result = await leaderboardService.GetAsync("steam-playtime", guildId, 10, cancellationToken);
            if (result.Entries.Count == 0)
            {
                continue;
            }

            var embed = new EmbedBuilder()
                .WithTitle($"📊 Wöchentliches Leaderboard — {result.Title}")
                .WithColor(Color.Gold)
                .WithTimestamp(DateTimeOffset.UtcNow)
                .WithFooter("WK7 Bot • Leaderboard");
            foreach (var entry in result.Entries)
            {
                embed.AddField($"#{entry.Rank} {entry.DisplayName}", $"{entry.Value}" + (string.IsNullOrWhiteSpace(entry.Detail) ? string.Empty : $" · {entry.Detail}"), inline: false);
            }

            await CardSender.SendCardAsync(channel, CardBuilder.FromEmbed(embed.Build()), embed.Build(), _logger, cancellationToken).ConfigureAwait(false);
        }

        _lastPostedWeek = week;
    }
}
