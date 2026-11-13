namespace WK7Bot.Services;

using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using WK7Bot.Core.Constants;
using WK7Bot.Core.Entities;
using WK7Bot.Core.Interfaces;
using WK7Bot.Core.Utilities;
using WK7Bot.Models;
using WK7Bot.Options;
using WK7Bot.Services.Interfaces;

/// <summary>
/// Polls Steam achievement progress for linked users and posts newly unlocked achievements
/// to the WK7 guild's achievement channel.
/// </summary>
public class AchievementUnlockDigestBackgroundService : BackgroundService
{
    /// <summary>Polling cadence for the digest.</summary>
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(30);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly DiscordSocketClient _discordClient;
    private readonly ISteamService _steamService;
    private readonly IFeatureHealthTracker _health;
    private readonly Wk7BotOptions _options;
    private readonly ILogger<AchievementUnlockDigestBackgroundService> _logger;

    /// <summary>Initializes a new instance of the <see cref="AchievementUnlockDigestBackgroundService"/> class.</summary>
    public AchievementUnlockDigestBackgroundService(
        IServiceScopeFactory scopeFactory,
        DiscordSocketClient discordClient,
        ISteamService steamService,
        IFeatureHealthTracker health,
        IOptions<Wk7BotOptions> options,
        ILogger<AchievementUnlockDigestBackgroundService> logger)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _discordClient = discordClient ?? throw new ArgumentNullException(nameof(discordClient));
        _steamService = steamService ?? throw new ArgumentNullException(nameof(steamService));
        _health = health ?? throw new ArgumentNullException(nameof(health));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (string.IsNullOrWhiteSpace(_options.SteamApiKey))
        {
            _logger.LogWarning("Achievement unlock digest is enabled but no Steam API key is configured.");
            return;
        }

        _logger.LogInformation("Starting Achievement Unlock Digest Background Service (polling every {Interval} minutes).", PollInterval.TotalMinutes);

        await BackgroundServiceResilience.RunWithResilienceAsync(
            iteration: RunDigestAsync,
            featureKey: "achievement_unlock_digest",
            health: _health,
            logger: _logger,
            interval: PollInterval,
            stoppingToken: stoppingToken).ConfigureAwait(false);
    }

    /// <summary>Runs a single digest pass.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    internal async Task RunDigestAsync(CancellationToken cancellationToken)
    {
        if (!_options.Servers.TryGetWk7ServerId(out var guildId))
        {
            _logger.LogDebug("WK7 server ID is not configured; skipping achievement unlock digest.");
            return;
        }

        var guild = _discordClient.GetGuild(guildId);
        if (guild is null)
        {
            _logger.LogWarning("WK7 guild {GuildId} is not available; skipping achievement unlock digest.", guildId);
            return;
        }

        var channel = guild.TextChannels
            .FirstOrDefault(c => c.Name.Contains("achievement", StringComparison.OrdinalIgnoreCase));

        if (channel is null)
        {
            _logger.LogWarning("No text channel whose name contains 'achievement' found in guild {GuildId}; achievement unlocks will not be posted.", guildId);
        }

        using var scope = _scopeFactory.CreateScope();
        var linkRepository = scope.ServiceProvider.GetRequiredService<IUserAccountLinkRepository>();
        var baselineRepository = scope.ServiceProvider.GetRequiredService<ISteamAchievementBaselineRepository>();

        var links = await linkRepository.GetAllAsync(cancellationToken).ConfigureAwait(false);
        var steamLinks = links
            .Where(l => string.Equals(l.Provider, AccountProviders.Steam, StringComparison.OrdinalIgnoreCase)
                        && !string.IsNullOrWhiteSpace(l.ExternalId))
            .ToList();

        if (steamLinks.Count == 0)
        {
            _logger.LogDebug("No linked Steam users found; skipping achievement unlock digest.");
            return;
        }

        List<(string GameName, SteamAchievement Achievement, ulong DiscordUserId)> newAchievements = [];

        foreach (var link in steamLinks)
        {
            var steamId = link.ExternalId;
            IReadOnlyList<(uint AppId, string Name)> ownedGames;
            try
            {
                ownedGames = await _steamService.GetOwnedGamesAsync(steamId, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to fetch owned games for Steam ID {SteamId}.", steamId);
                continue;
            }

            foreach (var game in ownedGames)
            {
                IReadOnlyList<SteamAchievement> achievements;
                try
                {
                    achievements = await _steamService.GetEnrichedAchievementsAsync(steamId, game.AppId, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to fetch achievements for AppId {AppId} and Steam ID {SteamId}.", game.AppId, steamId);
                    continue;
                }

                if (achievements.Count == 0)
                {
                    continue;
                }

                var baselineRows = await baselineRepository
                    .GetBaselineAsync(link.DiscordUserId, steamId, game.AppId, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
                var baselineByApi = baselineRows.ToDictionary(b => b.ApiName, StringComparer.OrdinalIgnoreCase);

                var now = DateTime.UtcNow;
                List<SteamAchievementBaseline> toUpsert = [];

                foreach (var achievement in achievements)
                {
                    var existing = baselineByApi.TryGetValue(achievement.ApiName, out var row) ? row : null;
                    var isNew = existing is null || !existing.Achieved;

                    if (isNew)
                    {
                        newAchievements.Add((string.IsNullOrWhiteSpace(game.Name) ? $"App {game.AppId}" : game.Name, achievement, link.DiscordUserId));
                    }

                    toUpsert.Add(new SteamAchievementBaseline
                    {
                        DiscordUserId = link.DiscordUserId,
                        SteamId = steamId,
                        AppId = game.AppId,
                        ApiName = achievement.ApiName,
                        Achieved = true,
                        UnlockTime = achievement.UnlockTime?.UtcDateTime,
                        LastChecked = now
                    });
                }

                if (toUpsert.Count > 0)
                {
                    try
                    {
                        await baselineRepository.UpsertManyAsync(toUpsert, cancellationToken).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to upsert achievement baseline for AppId {AppId}.", game.AppId);
                    }
                }
            }
        }

        if (newAchievements.Count == 0)
        {
            _logger.LogDebug("No new achievement unlocks detected.");
            return;
        }

        if (channel is null)
        {
            return;
        }

        var embed = BuildEmbed(newAchievements);
        try
        {
            await channel.SendMessageAsync(embed: embed).ConfigureAwait(false);
            _logger.LogInformation("Posted {Count} new achievement unlock(s) to {Channel}.", newAchievements.Count, channel.Name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to post achievement unlock digest to {Channel}.", channel.Name);
        }
    }

    private static Embed BuildEmbed(IReadOnlyList<(string GameName, SteamAchievement Achievement, ulong DiscordUserId)> items)
    {
        var builder = new EmbedBuilder()
            .WithTitle("🏆 Achievement Unlock Digest")
            .WithDescription("Newly unlocked Steam achievements.")
            .WithColor(Color.Gold)
            .WithTimestamp(DateTimeOffset.UtcNow)
            .WithFooter("WK7 Bot • Achievement Unlock Digest");

        var added = 0;
        foreach (var item in items)
        {
            if (added >= 25)
            {
                break;
            }

            var achievement = item.Achievement;
            var rarity = achievement.PercentUnlocked > 0
                ? $"{achievement.PercentUnlocked:F1}%"
                : "unknown";

            var value = $"Game: {item.GameName}\n" +
                        $"Rarity: {rarity}\n" +
                        (string.IsNullOrWhiteSpace(achievement.IconUrl) ? string.Empty : $"Icon: {achievement.IconUrl}\n") +
                        (item.DiscordUserId > 0 ? $"User: <@{item.DiscordUserId}>" : string.Empty);

            builder.AddField(achievement.Name, value, inline: false);
            added++;
        }

        return builder.Build();
    }
}
