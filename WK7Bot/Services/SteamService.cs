namespace WK7Bot.Services;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WK7Bot.Models;
using WK7Bot.Options;
using WK7Bot.Services.Interfaces;

/// <summary>
/// Communicates with Valve Steam Web API endpoints to fetch player profiles, recent activity, and achievement metrics.
/// </summary>
public class SteamService : ISteamService
{
    private readonly HttpClient _httpClient;
    private readonly Wk7BotOptions _options;
    private readonly ILogger<SteamService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="SteamService"/> class.
    /// </summary>
    /// <param name="httpClient">The HTTP client instance configured for Steam requests.</param>
    /// <param name="options">The strongly-typed application options instance.</param>
    /// <param name="logger">The logging service instance for operational diagnostics.</param>
    public SteamService(HttpClient httpClient, IOptions<Wk7BotOptions> options, ILogger<SteamService> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
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
    /// Fetches player summary data, recent game playtimes, and active game achievements for a specific Steam ID.
    /// </summary>
    /// <param name="steamId">The 64-bit Steam ID of the user.</param>
    /// <param name="cancellationToken">A cancellation token to monitor for task cancellation.</param>
    /// <returns>A populated <see cref="SteamUserData"/> model or null if fetching fails.</returns>
    public async Task<SteamUserData?> GetSteamUserDataAsync(string steamId, CancellationToken cancellationToken = default)
    {
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
                await EnrichRecentAchievementsAsync(userData, cancellationToken);
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
    /// Requests player summary state including display name, online status, and current active game.
    /// </summary>
    /// <param name="steamId">The 64-bit Steam ID target.</param>
    /// <param name="cancellationToken">A cancellation token to monitor for task cancellation.</param>
    /// <returns>An initialized <see cref="SteamUserData"/> container populated with summary metadata.</returns>
    private async Task<SteamUserData?> FetchPlayerSummaryAsync(string steamId, CancellationToken cancellationToken)
    {
        string url = $"https://api.steampowered.com/ISteamUser/GetPlayerSummaries/v0002/?key={_options.SteamApiKey}&steamids={steamId}";
        using var response = await _httpClient.GetAsync(url, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        var players = doc.RootElement.GetProperty("response").GetProperty("players");
        if (players.GetArrayLength() == 0)
        {
            return null;
        }

        var player = players[0];
        var data = new SteamUserData
        {
            SteamId = steamId,
            PersonaName = player.GetProperty("personaname").GetString() ?? string.Empty,
            PersonaState = MapPersonaState(player.GetProperty("personastate").GetInt32())
        };

        if (player.TryGetProperty("gameextrainfo", out var gameName))
        {
            data.CurrentGameTitle = gameName.GetString();
        }

        if (player.TryGetProperty("gameid", out var gameId) && uint.TryParse(gameId.GetString(), out uint appId))
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
        using var response = await _httpClient.GetAsync(url, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return;
        }

        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        var responseObj = doc.RootElement.GetProperty("response");

        if (!responseObj.TryGetProperty("games", out var games))
        {
            return;
        }

        int totalTwoWeekMinutes = 0;
        foreach (var game in games.EnumerateArray())
        {
            int twoWeeks = game.GetProperty("playtime_2weeks").GetInt32();
            totalTwoWeekMinutes += twoWeeks;

            userData.RecentGames.Add(new SteamRecentGame
            {
                AppId = game.GetProperty("appid").GetUInt32(),
                Name = game.GetProperty("name").GetString() ?? string.Empty,
                PlaytimeTwoWeeksMinutes = twoWeeks,
                PlaytimeForeverMinutes = game.GetProperty("playtime_forever").GetInt32()
            });
        }

        userData.PlaytimeLastTwoWeeksMinutes = totalTwoWeekMinutes;
    }

    /// <summary>
    /// Retrieves achievement completion progress for the currently active game.
    /// </summary>
    /// <param name="userData">The user data model containing active game context.</param>
    /// <param name="cancellationToken">A cancellation token to monitor for task cancellation.</param>
    /// <returns>A task tracking the asynchronous operation.</returns>
    private async Task EnrichRecentAchievementsAsync(SteamUserData userData, CancellationToken cancellationToken)
    {
        if (!userData.CurrentGameAppId.HasValue)
        {
            return;
        }

        string url = $"https://api.steampowered.com/ISteamUserStats/GetPlayerAchievements/v0001/?key={_options.SteamApiKey}&steamid={userData.SteamId}&appid={userData.CurrentGameAppId.Value}&l=en";
        using var response = await _httpClient.GetAsync(url, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return;
        }

        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        var playerStats = doc.RootElement.GetProperty("playerstats");

        if (!playerStats.TryGetProperty("achievements", out var achievements))
        {
            return;
        }

        var unlockedList = new List<SteamAchievement>();
        foreach (var ach in achievements.EnumerateArray())
        {
            if (ach.GetProperty("achieved").GetInt32() == 1)
            {
                long unlockTimestamp = ach.GetProperty("unlocktime").GetInt64();
                unlockedList.Add(new SteamAchievement
                {
                    ApiName = ach.GetProperty("apiname").GetString() ?? string.Empty,
                    Name = ach.TryGetProperty("name", out var n) ? n.GetString() ?? string.Empty : string.Empty,
                    Description = ach.TryGetProperty("description", out var d) ? d.GetString() ?? string.Empty : string.Empty,
                    UnlockTime = DateTimeOffset.FromUnixTimeSeconds(unlockTimestamp)
                });
            }
        }

        userData.RecentAchievements = unlockedList
            .OrderByDescending(a => a.UnlockTime)
            .Take(5)
            .ToList();
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