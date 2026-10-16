namespace WK7Bot.Services;

using Discord.WebSocket;
using Microsoft.Extensions.Options;
using WK7Bot.Core.Entities;
using WK7Bot.Core.Interfaces;
using WK7Bot.Models;
using WK7Bot.Options;
using WK7Bot.Services.Interfaces;

/// <summary>
/// Builds Steam and Battle.net leaderboards from the account links stored in the database and the
/// mappings in the add-on options.
/// </summary>
public class LeaderboardService : ILeaderboardService
{
    private readonly IUserAccountLinkRepository _linkRepository;
    private readonly ISteamService _steamService;
    private readonly IBattleNetService _battleNetService;
    private readonly DiscordSocketClient _discordClient;
    private readonly IOptions<Wk7BotOptions> _options;
    private readonly ILogger<LeaderboardService> _logger;

    /// <summary>Initializes a new instance of the <see cref="LeaderboardService"/> class.</summary>
    public LeaderboardService(
        IUserAccountLinkRepository linkRepository,
        ISteamService steamService,
        IBattleNetService battleNetService,
        DiscordSocketClient discordClient,
        IOptions<Wk7BotOptions> options,
        ILogger<LeaderboardService> logger)
    {
        _linkRepository = linkRepository ?? throw new ArgumentNullException(nameof(linkRepository));
        _steamService = steamService ?? throw new ArgumentNullException(nameof(steamService));
        _battleNetService = battleNetService ?? throw new ArgumentNullException(nameof(battleNetService));
        _discordClient = discordClient ?? throw new ArgumentNullException(nameof(discordClient));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<LeaderboardResult> GetAsync(string type, ulong guildId, int limit, CancellationToken cancellationToken = default)
    {
        var normalized = (type ?? "steam-playtime").Trim().ToLowerInvariant();
        return normalized switch
        {
            "steam-achievements" => await BuildSteamAsync(guildId, limit, achievements: true, cancellationToken).ConfigureAwait(false),
            "battlenet-wow" => await BuildBattleNetWowAsync(guildId, limit, cancellationToken).ConfigureAwait(false),
            "battlenet-diablo" => await BuildBattleNetDiabloAsync(guildId, limit, cancellationToken).ConfigureAwait(false),
            _ => await BuildSteamAsync(guildId, limit, achievements: false, cancellationToken).ConfigureAwait(false)
        };
    }

    private async Task<LeaderboardResult> BuildSteamAsync(ulong guildId, int limit, bool achievements, CancellationToken cancellationToken)
    {
        var links = await GetSteamLinksAsync(cancellationToken).ConfigureAwait(false);
        var rows = new List<(string Name, long Sort, string Value, string? Detail)>();
        foreach (var (discordUserId, steamId) in links)
        {
            var data = await _steamService.GetSteamUserDataAsync(steamId, cancellationToken).ConfigureAwait(false);
            if (data is null)
            {
                continue;
            }

            if (achievements)
            {
                var unlocked = data.RecentGames.Sum(g => g.AchievementsUnlocked);
                var total = data.RecentGames.Sum(g => g.AchievementsTotal);
                rows.Add((ResolveDisplayName(guildId, discordUserId), unlocked, $"{unlocked}/{total}", data.PersonaName));
            }
            else
            {
                rows.Add((ResolveDisplayName(guildId, discordUserId), data.PlaytimeLastTwoWeeksMinutes, SteamUserData.FormatPlaytime(data.PlaytimeLastTwoWeeksMinutes), data.PersonaName));
            }
        }

        return Rank(achievements ? "🏆 Steam-Errungenschaften (14 Tage)" : "⏱️ Steam-Spielzeit (14 Tage)", rows, limit, "WK7 Bot • Leaderboard");
    }

    private async Task<LeaderboardResult> BuildBattleNetWowAsync(ulong guildId, int limit, CancellationToken cancellationToken)
    {
        var links = await GetBattleNetLinksAsync(cancellationToken).ConfigureAwait(false);
        var rows = new List<(string Name, long Sort, string Value, string? Detail)>();
        foreach (var (discordUserId, refreshToken) in links)
        {
            var data = await _battleNetService.GetBattleNetUserDataAsync(refreshToken, cancellationToken).ConfigureAwait(false);
            var main = data?.WowCharacters.OrderByDescending(c => c.Level).FirstOrDefault();
            if (main is null)
            {
                continue;
            }

            rows.Add((ResolveDisplayName(guildId, discordUserId), main.Level, $"Stufe {main.Level}", $"{main.Name} · {main.CharacterClass}"));
        }

        return Rank("⚔️ World of Warcraft — höchste Stufe", rows, limit, "WK7 Bot • Leaderboard");
    }

    private async Task<LeaderboardResult> BuildBattleNetDiabloAsync(ulong guildId, int limit, CancellationToken cancellationToken)
    {
        var links = await GetBattleNetLinksAsync(cancellationToken).ConfigureAwait(false);
        var rows = new List<(string Name, long Sort, string Value, string? Detail)>();
        foreach (var (discordUserId, refreshToken) in links)
        {
            var data = await _battleNetService.GetBattleNetUserDataAsync(refreshToken, cancellationToken).ConfigureAwait(false);
            var hero = data?.DiabloHeroes.OrderByDescending(h => h.ParagonLevel).ThenByDescending(h => h.Level).FirstOrDefault();
            if (hero is null)
            {
                continue;
            }

            var score = hero.ParagonLevel > 0 ? hero.ParagonLevel : hero.Level;
            rows.Add((ResolveDisplayName(guildId, discordUserId), score, hero.ParagonLevel > 0 ? $"Paragon {hero.ParagonLevel}" : $"Stufe {hero.Level}", $"{hero.Name} · {hero.HeroClass}"));
        }

        return Rank("😈 Diablo — höchster Paragon/Level", rows, limit, "WK7 Bot • Leaderboard");
    }

    private async Task<List<(ulong DiscordUserId, string SteamId)>> GetSteamLinksAsync(CancellationToken cancellationToken)
    {
        var result = new Dictionary<ulong, string>();
        foreach (var mapping in _options.Value.DiscordSteamMappings)
        {
            if (ulong.TryParse(mapping.DiscordUserId, out var id) && !string.IsNullOrWhiteSpace(mapping.SteamId))
            {
                result[id] = mapping.SteamId;
            }
        }

        foreach (var link in await _linkRepository.GetAllAsync(cancellationToken).ConfigureAwait(false))
        {
            if (link.Provider == "steam" && !string.IsNullOrWhiteSpace(link.ExternalId))
            {
                result[link.DiscordUserId] = link.ExternalId;
            }
        }

        return result.Select(kv => (kv.Key, kv.Value)).ToList();
    }

    private async Task<List<(ulong DiscordUserId, string RefreshToken)>> GetBattleNetLinksAsync(CancellationToken cancellationToken)
    {
        var result = new Dictionary<ulong, string>();
        foreach (var mapping in _options.Value.DiscordBattleNetMappings)
        {
            if (ulong.TryParse(mapping.DiscordUserId, out var id) && !string.IsNullOrWhiteSpace(mapping.RefreshToken))
            {
                result[id] = mapping.RefreshToken;
            }
        }

        foreach (var link in await _linkRepository.GetAllAsync(cancellationToken).ConfigureAwait(false))
        {
            if (link.Provider == "battlenet" && !string.IsNullOrWhiteSpace(link.RefreshToken))
            {
                result[link.DiscordUserId] = link.RefreshToken!;
            }
        }

        return result.Select(kv => (kv.Key, kv.Value)).ToList();
    }

    private string ResolveDisplayName(ulong guildId, ulong discordUserId)
    {
        var guild = _discordClient.GetGuild(guildId);
        return guild?.GetUser(discordUserId)?.DisplayName ?? discordUserId.ToString();
    }

    private static LeaderboardResult Rank(string title, List<(string Name, long Sort, string Value, string? Detail)> rows, int limit, string footer)
    {
        var entries = rows
            .OrderByDescending(r => r.Sort)
            .ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            .Take(Math.Clamp(limit, 1, 25))
            .Select((r, index) => new LeaderboardEntry(index + 1, r.Name, r.Value, r.Detail))
            .ToList();

        return new LeaderboardResult(title, entries, footer);
    }
}
