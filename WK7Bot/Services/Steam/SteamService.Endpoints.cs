using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WK7Bot.Models;
using WK7Bot.Models.Steam;
using WK7Bot.Options;

namespace WK7Bot.Services;

public partial class SteamService
{
    /// <summary>
    /// Requests player summary state including display name, online status, and current active game.
    /// </summary>
    /// <param name="steamId">The 64-bit Steam ID target.</param>
    /// <param name="cancellationToken">A cancellation token to monitor for task cancellation.</param>
    /// <returns>An initialized <see cref="SteamUserData"/> container populated with summary metadata, or null if request fails.</returns>
    private async Task<SteamUserData?> FetchPlayerSummaryAsync(string steamId, CancellationToken cancellationToken)
    {
        string url = $"https://api.steampowered.com/ISteamUser/GetPlayerSummaries/v0002/?key={_options.SteamApiKey}&steamids={steamId}";

        using var response = await GetSteamApiWithRetryAsync(url, "GetPlayerSummaries", cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "Steam API returned status code {StatusCode} for GetPlayerSummaries (SteamId: {SteamId})",
                response.StatusCode,
                steamId);
            return null;
        }

        var result = await response.Content.ReadFromJsonAsync<SteamPlayerSummariesResponse>(cancellationToken: cancellationToken);

        var player = result?.Response?.Players?.FirstOrDefault();
        if (player == null)
        {
            return null;
        }

        var data = new SteamUserData
        {
            SteamId = steamId,
            PersonaName = player.PersonaName ?? string.Empty,
            PersonaState = MapPersonaState(player.PersonaState),
            SteamAvatarUrl = string.IsNullOrWhiteSpace(player.AvatarFull) ? null : player.AvatarFull
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

        using var response = await GetSteamApiWithRetryAsync(url, "GetRecentlyPlayedGames", cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "Steam API returned status code {StatusCode} for GetRecentlyPlayedGames (SteamId: {SteamId})",
                response.StatusCode,
                userData.SteamId);
            return;
        }

        var result = await response.Content.ReadFromJsonAsync<SteamRecentlyPlayedGamesResponse>(cancellationToken: cancellationToken);

        var games = result?.Response?.Games;
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
    /// Retrieves game achievement schema metadata from the Steam Web API or cache, mapping API names to display names, descriptions, and icon URLs.
    /// </summary>
    /// <param name="appId">The unique application identifier for the game.</param>
    /// <param name="cancellationToken">A token to monitor for operation cancellation.</param>
    /// <returns>A dictionary mapping achievement API names to their schema definition containing display properties.</returns>
    private async Task<IReadOnlyDictionary<string, SchemaAchievementItem>> GetGameSchemaAsync(uint appId, CancellationToken cancellationToken)
    {
        var cacheKey = $"steam_schema_achievements_v2_{appId}";

        if (_cache.TryGetValue<IReadOnlyDictionary<string, SchemaAchievementItem>>(cacheKey, out var cached) && cached != null)
        {
            return cached;
        }

        try
        {
            string url = $"https://api.steampowered.com/ISteamUserStats/GetSchemaForGame/v2/?key={_options.SteamApiKey}&appid={appId}&l=english";
            using var response = await GetSteamApiWithRetryAsync(url, "GetSchemaForGame", cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Steam API returned status code {StatusCode} for GetSchemaForGame (AppId: {AppId}). Schema is not cached.",
                    response.StatusCode,
                    appId);
                return new Dictionary<string, SchemaAchievementItem>();
            }

            var schemaResponse = await response.Content.ReadFromJsonAsync<SteamSchemaResponse>(cancellationToken: cancellationToken);

            var achievements = schemaResponse?.Game?.AvailableGameStats?.Achievements;
            if (achievements == null || achievements.Count == 0)
            {
                var emptySchema = new Dictionary<string, SchemaAchievementItem>();
                _cache.Set(cacheKey, emptySchema, TimeSpan.FromHours(24));
                return emptySchema;
            }

            var schemaMap = achievements
                .GroupBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    g => g.Key,
                    g => g.First(),
                    StringComparer.OrdinalIgnoreCase);

            _cache.Set(cacheKey, schemaMap, TimeSpan.FromHours(24));
            return schemaMap;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Failed to retrieve game schema for AppId {AppId}. Schema is not cached.", appId);
            return new Dictionary<string, SchemaAchievementItem>();
        }
    }

    /// <summary>
    /// Requests player achievement progress and unlock timestamps from the Steam Web API.
    /// </summary>
    /// <param name="steamId">The 64-bit Steam ID of the user.</param>
    /// <param name="appId">The application identifier for the game.</param>
    /// <param name="cancellationToken">A token to monitor for operation cancellation.</param>
    /// <returns>A list of unlocked player achievements populated with API names, titles, descriptions, and unlock times.</returns>
    private async Task<List<SteamAchievement>> GetPlayerAchievementsAsync(string steamId, uint appId, CancellationToken cancellationToken)
    {
        string url = $"https://api.steampowered.com/ISteamUserStats/GetPlayerAchievements/v0001/?key={_options.SteamApiKey}&steamid={steamId}&appid={appId}&l=english";

        using var response = await GetSteamApiWithRetryAsync(url, "GetPlayerAchievements", cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            if (response.StatusCode == HttpStatusCode.Forbidden)
            {
                var body = (await response.Content.ReadAsStringAsync(cancellationToken)).Trim();
                var bodySnippet = string.IsNullOrEmpty(body)
                    ? "(empty)"
                    : body.Length > 200 ? body[..200] : body;

                _logger.LogWarning(
                    "Steam API returned 403 Forbidden for GetPlayerAchievements (AppId: {AppId}, SteamId: {SteamId}). "
                    + "This usually means the target user's 'Game details' privacy setting is not Public (friends-only is "
                    + "not enough for the Web API); falling back to GetTopAchievementsForGames, which still returns "
                    + "unlocked achievements but without unlock timestamps. Body: {Body}",
                    appId,
                    steamId,
                    bodySnippet);

                var fallback = await GetTopAchievementsFallbackAsync(steamId, appId, cancellationToken);
                if (fallback.Count > 0)
                {
                    _logger.LogInformation(
                        "GetTopAchievementsForGames fallback recovered {Count} unlocked achievement(s) (AppId: {AppId}, SteamId: {SteamId}).",
                        fallback.Count,
                        appId,
                        steamId);
                }

                return fallback;
            }

            _logger.LogWarning("Steam API returned status code {StatusCode} for GetPlayerAchievements (AppId: {AppId}, SteamId: {SteamId})", response.StatusCode, appId, steamId);
            return new List<SteamAchievement>();
        }

        var options = new JsonSerializerOptions
        {
            NumberHandling = JsonNumberHandling.AllowReadingFromString,
            PropertyNameCaseInsensitive = true
        };

        var achievementResponse = await response.Content.ReadFromJsonAsync<SteamPlayerAchievementsResponse>(options, cancellationToken);

        var playerStats = achievementResponse?.PlayerStats;
        if (playerStats == null || !playerStats.Success || playerStats.Achievements == null)
        {
            _logger.LogWarning(
                "GetPlayerAchievements returned no usable data (AppId: {AppId}, SteamId: {SteamId}). Success: {Success}, Error: {Error}",
                appId,
                steamId,
                playerStats?.Success,
                playerStats?.Error ?? "none");
            return new List<SteamAchievement>();
        }

        var results = new List<SteamAchievement>();

        foreach (var item in playerStats.Achievements.Where(a => a.IsAchieved))
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
    /// Fallback for users whose 'Game details' privacy blocks <c>GetPlayerAchievements</c> (HTTP 403):
    /// retrieves the user's unlocked achievements via <c>IPlayerService/GetTopAchievementsForGames</c>,
    /// which honors only profile-level visibility. The response carries display names, descriptions, and
    /// icon file names but no unlock timestamps, so <see cref="SteamAchievement.UnlockTime"/> stays <see langword="null"/>.
    /// </summary>
    /// <param name="steamId">The 64-bit Steam ID of the user.</param>
    /// <param name="appId">The application identifier for the game.</param>
    /// <param name="cancellationToken">A token to monitor for operation cancellation.</param>
    /// <returns>The unlocked achievements reported by the fallback endpoint, or an empty list when unavailable.</returns>
    private async Task<List<SteamAchievement>> GetTopAchievementsFallbackAsync(string steamId, uint appId, CancellationToken cancellationToken)
    {
        string url = $"https://api.steampowered.com/IPlayerService/GetTopAchievementsForGames/v1/?key={_options.SteamApiKey}&steamid={steamId}&language=en&max_achievements=1000&appids[0]={appId}";

        using var response = await GetSteamApiWithRetryAsync(url, "GetTopAchievementsForGames", cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "Steam API returned status code {StatusCode} for GetTopAchievementsForGames fallback (AppId: {AppId}, SteamId: {SteamId})",
                response.StatusCode,
                appId,
                steamId);
            return new List<SteamAchievement>();
        }

        var payload = await response.Content.ReadFromJsonAsync<SteamTopAchievementsResponse>(cancellationToken: cancellationToken);
        var game = payload?.Response?.Games?.FirstOrDefault(g => g.AppId == appId);
        if (game?.Achievements == null || game.Achievements.Count == 0)
        {
            return new List<SteamAchievement>();
        }

        var results = new List<SteamAchievement>();

        foreach (var item in game.Achievements)
        {
            var displayName = !string.IsNullOrWhiteSpace(item.Name) ? item.Name : string.Empty;
            var iconUrl = string.IsNullOrEmpty(item.Icon)
                ? string.Empty
                : $"https://steamcdn-a.akamaihd.net/steamcommunity/public/images/apps/{appId}/{item.Icon}";

            results.Add(new SteamAchievement
            {
                ApiName = displayName,
                Name = displayName,
                Description = item.Desc ?? string.Empty,
                UnlockTime = null,
                IconUrl = iconUrl,
                Hidden = item.Hidden,
                PercentUnlocked = item.PercentUnlocked
            });
        }

        return results;
    }

}
