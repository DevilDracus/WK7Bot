namespace WK7Bot.Services;

using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using WK7Bot.Models;
using WK7Bot.Options;
using WK7Bot.Services.Interfaces;

/// <summary>
/// Communicates with Valve Steam Web API endpoints to fetch player profiles, recent activity, and enriched achievement metrics.
/// </summary>
public class SteamService : ISteamService
{
    private readonly HttpClient _httpClient;
    private readonly IMemoryCache _cache;
    private readonly Wk7BotOptions _options;
    private readonly ILogger<SteamService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="SteamService"/> class.
    /// </summary>
    /// <param name="httpClient">The HTTP client instance configured for Steam requests.</param>
    /// <param name="cache">The memory cache instance used to cache game achievement schemas.</param>
    /// <param name="options">The strongly-typed application options instance containing credentials and mappings.</param>
    /// <param name="logger">The logging service instance for operational diagnostics.</param>
    /// <exception cref="ArgumentNullException">Thrown when any required dependency is null.</exception>
    public SteamService(
        HttpClient httpClient,
        IMemoryCache cache,
        IOptions<Wk7BotOptions> options,
        ILogger<SteamService> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    }

    /// <summary>
    /// Resolves the configured Steam ID for a specified Discord user snowflake ID.
    /// </summary>
    /// <param name="discordUserId">The target Discord user ID.</param>
    /// <returns>The mapped 64-bit Steam ID, or null if no mapping exists.</returns>
    public string? GetSteamIdForDiscordUser(string discordUserId)
    {
        return _options.DiscordSteamMappings
            .FirstOrDefault(m => string.Equals(m.DiscordUserId, discordUserId, StringComparison.OrdinalIgnoreCase))
            ?.SteamId;
    }

    /// <summary>
    /// Fetches player summary data, recent game playtimes, and enriched active game achievements for a specific Steam ID.
    /// </summary>
    /// <param name="steamId">The 64-bit Steam ID of the user.</param>
    /// <param name="cancellationToken">A cancellation token to monitor for task cancellation.</param>
    /// <returns>A populated <see cref="SteamUserData"/> model, or null if retrieval fails.</returns>
    public async Task<SteamUserData?> GetSteamUserDataAsync(string steamId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(steamId);

        if (string.IsNullOrWhiteSpace(_options.SteamApiKey))
        {
            _logger.LogWarning("Steam API key is missing in configuration. Skipping Steam data retrieval.");
            return null;
        }

        try
        {
            var userData = await FetchPlayerSummaryAsync(steamId, cancellationToken);
            if (userData == null)
            {
                return null;
            }

            await EnrichRecentGamesAsync(userData, cancellationToken);

            if (userData.CurrentGameAppId.HasValue)
            {
                var achievements = await GetEnrichedAchievementsAsync(steamId, userData.CurrentGameAppId.Value, cancellationToken);
                userData.RecentAchievements = achievements
                    .OrderByDescending(a => a.UnlockTime)
                    .Take(5)
                    .ToList();
            }

            return userData;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred while fetching Steam data for Steam ID {SteamId}", steamId);
            return null;
        }
    }

    /// <summary>
    /// Retrieves player achievements for a specified game and merges icon image URLs obtained from the Steam Game Schema API.
    /// </summary>
    /// <param name="steamId">The unique 64-bit Steam identifier of the target user.</param>
    /// <param name="appId">The unique application identifier for the target game.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A collection of enriched <see cref="SteamAchievement"/> instances containing achievement info, timestamps, and icon URLs.</returns>
    public async Task<IReadOnlyList<SteamAchievement>> GetEnrichedAchievementsAsync(
        string steamId,
        uint appId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(steamId);

        try
        {
            var schemaTask = GetGameSchemaAsync(appId, cancellationToken);
            var playerAchievementsTask = GetPlayerAchievementsAsync(steamId, appId, cancellationToken);

            await Task.WhenAll(schemaTask, playerAchievementsTask);

            var schemaIcons = schemaTask.Result;
            var playerAchievements = playerAchievementsTask.Result;

            foreach (var achievement in playerAchievements)
            {
                if (schemaIcons.TryGetValue(achievement.ApiName, out var iconUrl))
                {
                    achievement.IconUrl = iconUrl;
                }
            }

            return playerAchievements;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve enriched achievements for AppId {AppId} and SteamID {SteamId}", appId, steamId);
            return Array.Empty<SteamAchievement>();
        }
    }

    /// <summary>
    /// Requests player summary state including display name, online status, and current active game.
    /// </summary>
    /// <param name="steamId">The 64-bit Steam ID target.</param>
    /// <param name="cancellationToken">A cancellation token to monitor for task cancellation.</param>
    /// <returns>An initialized <see cref="SteamUserData"/> container populated with summary metadata, or null if request fails.</returns>
    private async Task<SteamUserData?> FetchPlayerSummaryAsync(string steamId, CancellationToken cancellationToken)
    {
        string url = $"https://api.steampowered.com/ISteamUser/GetPlayerSummaries/v0002/?key={_options.SteamApiKey}&steamids={steamId}";
        var response = await _httpClient.GetFromJsonAsync<SteamPlayerSummariesResponse>(url, cancellationToken);

        var player = response?.Response?.Players?.FirstOrDefault();
        if (player == null)
        {
            return null;
        }

        var data = new SteamUserData
        {
            SteamId = steamId,
            PersonaName = player.PersonaName ?? string.Empty,
            PersonaState = MapPersonaState(player.PersonaState)
        };

        if (!string.IsNullOrWhiteSpace(player.GameExtraInfo))
        {
            data.CurrentGameTitle = player.GameExtraInfo;
        }

        if (!string.IsNullOrWhiteSpace(player.GameId) && uint.TryParse(player.GameId, out uint appId))
        {
            data.CurrentGameAppId = appId;
        }

        return data;
    }

    /// <summary>
    /// Retrieves recent game play times for the user over the prior 14 days.
    /// </summary>
    /// <param name="userData">The user data model to populate with recent game statistics.</param>
    /// <param name="cancellationToken">A cancellation token to monitor for task cancellation.</param>
    /// <returns>A task tracking the asynchronous operation.</returns>
    private async Task EnrichRecentGamesAsync(SteamUserData userData, CancellationToken cancellationToken)
    {
        string url = $"https://api.steampowered.com/IPlayerService/GetRecentlyPlayedGames/v0001/?key={_options.SteamApiKey}&steamid={userData.SteamId}&format=json";
        var response = await _httpClient.GetFromJsonAsync<SteamRecentlyPlayedGamesResponse>(url, cancellationToken);

        var games = response?.Response?.Games;
        if (games == null || games.Count == 0)
        {
            return;
        }

        int totalTwoWeekMinutes = 0;
        foreach (var game in games)
        {
            totalTwoWeekMinutes += game.PlaytimeTwoWeeks;

            userData.RecentGames.Add(new SteamRecentGame
            {
                AppId = game.AppId,
                Name = game.Name ?? string.Empty,
                PlaytimeTwoWeeksMinutes = game.PlaytimeTwoWeeks,
                PlaytimeForeverMinutes = game.PlaytimeForever
            });
        }

        userData.PlaytimeLastTwoWeeksMinutes = totalTwoWeekMinutes;
    }

    /// <summary>
    /// Retrieves game achievement schema mappings from the Steam Web API or cache, linking API names to unlocked icon URLs.
    /// </summary>
    /// <param name="appId">The unique application identifier for the game.</param>
    /// <param name="cancellationToken">A token to monitor for operation cancellation.</param>
    /// <returns>A dictionary mapping achievement API names to their respective absolute icon URLs.</returns>
    private async Task<IReadOnlyDictionary<string, string>> GetGameSchemaAsync(uint appId, CancellationToken cancellationToken)
    {
        var cacheKey = $"steam_schema_achievements_{appId}";

        return await _cache.GetOrCreateAsync(cacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(24);

            string url = $"https://api.steampowered.com/ISteamUserStats/GetSchemaForGame/v2/?key={_options.SteamApiKey}&appid={appId}";
            var response = await _httpClient.GetFromJsonAsync<SteamSchemaResponse>(url, cancellationToken);

            var achievements = response?.Game?.AvailableGameStats?.Achievements;
            if (achievements == null)
            {
                return new Dictionary<string, string>();
            }

            return achievements.ToDictionary(
                a => a.Name,
                a => a.Icon,
                StringComparer.OrdinalIgnoreCase);
        }) ?? new Dictionary<string, string>();
    }

    /// <summary>
    /// Requests player achievement progress and unlock timestamps from the Steam Web API.
    /// </summary>
    /// <param name="steamId">The 64-bit Steam ID of the user.</param>
    /// <param name="appId">The application identifier for the game.</param>
    /// <param name="cancellationToken">A token to monitor for operation cancellation.</param>
    /// <returns>A list of player achievements populated with API names, titles, descriptions, and unlock times.</returns>
    private async Task<List<SteamAchievement>> GetPlayerAchievementsAsync(string steamId, uint appId, CancellationToken cancellationToken)
    {
        string url = $"https://api.steampowered.com/ISteamUserStats/GetPlayerAchievements/v0001/?key={_options.SteamApiKey}&steamid={steamId}&appid={appId}&l=english";
        var response = await _httpClient.GetFromJsonAsync<SteamPlayerAchievementsResponse>(url, cancellationToken);

        var playerStats = response?.PlayerStats?.Achievements;
        if (playerStats == null)
        {
            return new List<SteamAchievement>();
        }

        var results = new List<SteamAchievement>();
        foreach (var item in playerStats.Where(a => a.Achieved == 1))
        {
            DateTimeOffset? unlockDateTime = item.UnlockTime > 0
                ? DateTimeOffset.FromUnixTimeSeconds(item.UnlockTime)
                : null;

            results.Add(new SteamAchievement
            {
                ApiName = item.ApiName,
                Name = !string.IsNullOrWhiteSpace(item.Name) ? item.Name : item.ApiName,
                Description = item.Description ?? string.Empty,
                UnlockTime = unlockDateTime
            });
        }

        return results;
    }

    /// <summary>
    /// Converts numeric Steam persona state values to human-readable status labels.
    /// </summary>
    /// <param name="stateCode">The integer status code returned by the Steam API.</param>
    /// <returns>A string representation of the user's online state.</returns>
    private static string MapPersonaState(int stateCode)
    {
        return stateCode switch
        {
            0 => "Offline",
            1 => "Online",
            2 => "Busy",
            3 => "Away",
            4 => "Snooze",
            5 => "LookingToTrade",
            6 => "LookingToPlay",
            _ => "Unknown"
        };
    }
}

#region JSON DTO Models

internal class SteamPlayerSummariesResponse
{
    [JsonPropertyName("response")]
    public PlayerSummaryContainer? Response { get; set; }
}

internal class PlayerSummaryContainer
{
    [JsonPropertyName("players")]
    public List<PlayerSummaryItem>? Players { get; set; }
}

internal class PlayerSummaryItem
{
    [JsonPropertyName("personaname")]
    public string? PersonaName { get; set; }

    [JsonPropertyName("personastate")]
    public int PersonaState { get; set; }

    [JsonPropertyName("gameextrainfo")]
    public string? GameExtraInfo { get; set; }

    [JsonPropertyName("gameid")]
    public string? GameId { get; set; }
}

internal class SteamRecentlyPlayedGamesResponse
{
    [JsonPropertyName("response")]
    public RecentlyPlayedGamesContainer? Response { get; set; }
}

internal class RecentlyPlayedGamesContainer
{
    [JsonPropertyName("games")]
    public List<RecentlyPlayedGameItem>? Games { get; set; }
}

internal class RecentlyPlayedGameItem
{
    [JsonPropertyName("appid")]
    public uint AppId { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("playtime_2weeks")]
    public int PlaytimeTwoWeeks { get; set; }

    [JsonPropertyName("playtime_forever")]
    public int PlaytimeForever { get; set; }
}

internal class SteamSchemaResponse
{
    [JsonPropertyName("game")]
    public SchemaGameData? Game { get; set; }
}

internal class SchemaGameData
{
    [JsonPropertyName("availableGameStats")]
    public SchemaStatsData? AvailableGameStats { get; set; }
}

internal class SchemaStatsData
{
    [JsonPropertyName("achievements")]
    public List<SchemaAchievementItem>? Achievements { get; set; }
}

internal class SchemaAchievementItem
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("icon")]
    public string Icon { get; set; } = string.Empty;
}

internal class SteamPlayerAchievementsResponse
{
    [JsonPropertyName("playerstats")]
    public PlayerStatsData? PlayerStats { get; set; }
}

internal class PlayerStatsData
{
    [JsonPropertyName("achievements")]
    public List<PlayerAchievementItem>? Achievements { get; set; }
}

internal class PlayerAchievementItem
{
    [JsonPropertyName("apiname")]
    public string ApiName { get; set; } = string.Empty;

    [JsonPropertyName("achieved")]
    public int Achieved { get; set; }

    [JsonPropertyName("unlocktime")]
    public long UnlockTime { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }
}

#endregion