using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WK7Bot.Models;
using WK7Bot.Options;
using WK7Bot.Services.Interfaces;

namespace WK7Bot.Services;

/// <summary>
/// Communicates with Valve Steam Web API endpoints to fetch player profiles, recent activity, and enriched achievement metrics.
/// </summary>
public class SteamService : ISteamService
{
    private const int MaxAttempts = 3;

    private readonly HttpClient _httpClient;
    private readonly IMemoryCache _cache;
    private readonly Wk7BotOptions _options;
    private readonly ILogger<SteamService> _logger;
    private readonly SemaphoreSlim _achievementApiThrottle = new(2, 2);

    /// <summary>
    /// Gets the base delay applied between retry attempts for transient Steam API failures. Virtual for testability.
    /// </summary>
    protected virtual TimeSpan RetryDelay => TimeSpan.FromMilliseconds(500);

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
    /// Fetches player summary data, recent game playtimes, and globally enriched active or recently played game achievements for a specific Steam ID.
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

            var appIdsToFetch = new HashSet<uint>();

            if (userData.CurrentGameAppId.HasValue)
            {
                appIdsToFetch.Add(userData.CurrentGameAppId.Value);
            }

            foreach (var recentGame in userData.RecentGames.Take(5))
            {
                appIdsToFetch.Add(recentGame.AppId);
            }

            if (appIdsToFetch.Count == 0)
            {
                _logger.LogInformation(
                    "No current game and no recently played games for Steam ID {SteamId}; skipping achievement fetch.",
                    steamId);
            }
            else
            {
                var appIds = appIdsToFetch.ToArray();
                var fetchTasks = appIds.Select(appId => GetEnrichedAchievementsAsync(steamId, appId, cancellationToken));
                var achievementsArrays = await Task.WhenAll(fetchTasks);

                for (int i = 0; i < appIds.Length; i++)
                {
                    _logger.LogDebug(
                        "Steam achievements for AppId {AppId}: {Count} unlocked for Steam ID {SteamId}",
                        appIds[i],
                        achievementsArrays[i].Count,
                        steamId);
                }

                var progressByAppId = new Dictionary<uint, (int Unlocked, int Total)>();
                for (int i = 0; i < appIds.Length; i++)
                {
                    var schema = await GetGameSchemaAsync(appIds[i], cancellationToken);
                    int unlocked = achievementsArrays[i].Count;
                    int total = schema.Count;
                    progressByAppId[appIds[i]] = (unlocked, total);

                    if (userData.CurrentGameAppId.HasValue
                        && userData.CurrentGameAppId.Value == appIds[i]
                        && total > 0)
                    {
                        userData.CurrentGameAchievementsUnlocked = unlocked;
                        userData.CurrentGameAchievementsTotal = total;
                    }
                }

                foreach (var recentGame in userData.RecentGames)
                {
                    if (progressByAppId.TryGetValue(recentGame.AppId, out var progress) && progress.Total > 0)
                    {
                        recentGame.AchievementsUnlocked = progress.Unlocked;
                        recentGame.AchievementsTotal = progress.Total;
                    }
                }

                userData.RecentAchievements = achievementsArrays
                    .SelectMany(a => a)
                    .OrderByDescending(a => a.UnlockTime)
                    .Take(5)
                    .ToList();

                _logger.LogDebug(
                    "Steam ID {SteamId}: {Unlocked} recent achievements selected from {Apps} games.",
                    steamId,
                    userData.RecentAchievements.Count,
                    appIds.Length);
            }

            return userData;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred while fetching Steam data for Steam ID {SteamId}", steamId);
            return null;
        }
    }

    /// <summary>
    /// Retrieves player achievements for a specified game and merges human-readable titles, descriptions, and icon image URLs from the Steam Game Schema API.
    /// </summary>
    /// <param name="steamId">The unique 64-bit Steam identifier of the target user.</param>
    /// <param name="appId">The unique application identifier for the target game.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A collection of enriched <see cref="SteamAchievement"/> instances containing achievement metadata, timestamps, and icon URLs.</returns>
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

            var schemaMap = schemaTask.Result;
            var playerAchievements = playerAchievementsTask.Result;

            foreach (var achievement in playerAchievements)
            {
                if (schemaMap.TryGetValue(achievement.ApiName, out var schemaItem))
                {
                    if (!string.IsNullOrWhiteSpace(schemaItem.Icon))
                    {
                        achievement.IconUrl = schemaItem.Icon;
                    }

                    if (!string.IsNullOrWhiteSpace(schemaItem.DisplayName))
                    {
                        achievement.Name = schemaItem.DisplayName;
                    }

                    if (!string.IsNullOrWhiteSpace(schemaItem.Description))
                    {
                        achievement.Description = schemaItem.Description;
                    }

                    achievement.Hidden = schemaItem.Hidden;
                }
            }

            return playerAchievements;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
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

    /// <summary>
    /// Executes a Steam Web API GET request while limiting achievement endpoint concurrency and retrying transient failures (429/5xx) with linear backoff.
    /// </summary>
    /// <param name="url">The fully qualified request URL.</param>
    /// <param name="operationName">The logical Steam operation name used in log messages.</param>
    /// <param name="cancellationToken">A cancellation token to monitor for cancellation requests.</param>
    /// <returns>The final <see cref="HttpResponseMessage"/>; the caller owns and must dispose it.</returns>
    private async Task<HttpResponseMessage> GetSteamApiWithRetryAsync(string url, string operationName, CancellationToken cancellationToken)
    {
        HttpResponseMessage response = await SendThrottledAsync(url, cancellationToken);

        for (int attempt = 1; attempt < MaxAttempts && IsTransientFailure(response); attempt++)
        {
            response.Dispose();

            var delay = TimeSpan.FromMilliseconds(RetryDelay.TotalMilliseconds * attempt);
            _logger.LogWarning(
                "Steam API {Operation} returned a transient failure (attempt {Attempt}/{MaxAttempts}); retrying after {Delay}.",
                operationName,
                attempt,
                MaxAttempts,
                delay);

            await Task.Delay(delay, cancellationToken);
            response = await SendThrottledAsync(url, cancellationToken);
        }

        if (IsTransientFailure(response))
        {
            _logger.LogWarning(
                "Steam API {Operation} still failing with status code {StatusCode} after {MaxAttempts} attempts.",
                operationName,
                response.StatusCode,
                MaxAttempts);
        }

        return response;
    }

    /// <summary>
    /// Performs a GET request under the shared achievement API concurrency throttle.
    /// </summary>
    /// <param name="url">The fully qualified request URL.</param>
    /// <param name="cancellationToken">A cancellation token to monitor for cancellation requests.</param>
    /// <returns>The received <see cref="HttpResponseMessage"/>.</returns>
    private async Task<HttpResponseMessage> SendThrottledAsync(string url, CancellationToken cancellationToken)
    {
        await _achievementApiThrottle.WaitAsync(cancellationToken);
        try
        {
            return await _httpClient.GetAsync(url, cancellationToken);
        }
        finally
        {
            _achievementApiThrottle.Release();
        }
    }

    /// <summary>
    /// Determines whether a response represents a transient Steam API failure eligible for retry (429 or 5xx).
    /// </summary>
    /// <param name="response">The response to evaluate.</param>
    /// <returns>True when the request should be retried; otherwise false.</returns>
    private static bool IsTransientFailure(HttpResponseMessage response)
    {
        return response.StatusCode == HttpStatusCode.TooManyRequests
            || (int)response.StatusCode >= 500;
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

/// <summary>
/// Root container model for the Steam GetPlayerSummaries Web API response.
/// </summary>
internal class SteamPlayerSummariesResponse
{
    /// <summary>
    /// Gets or sets the player summary container returned by Steam.
    /// </summary>
    [JsonPropertyName("response")]
    public PlayerSummaryContainer? Response { get; set; }
}

/// <summary>
/// Inner list container holding player summary items.
/// </summary>
internal class PlayerSummaryContainer
{
    /// <summary>
    /// Gets or sets the list of player summaries.
    /// </summary>
    [JsonPropertyName("players")]
    public List<PlayerSummaryItem>? Players { get; set; }
}

/// <summary>
/// Represents individual player summary attributes returned by Steam.
/// </summary>
internal class PlayerSummaryItem
{
    /// <summary>
    /// Gets or sets the display persona name of the player.
    /// </summary>
    [JsonPropertyName("personaname")]
    public string? PersonaName { get; set; }

    /// <summary>
    /// Gets or sets the absolute URL of the player's full-size Steam avatar.
    /// </summary>
    [JsonPropertyName("avatarfull")]
    public string? AvatarFull { get; set; }

    /// <summary>
    /// Gets or sets the numeric online status indicator.
    /// </summary>
    [JsonPropertyName("personastate")]
    public int PersonaState { get; set; }

    /// <summary>
    /// Gets or sets extra display information for the currently active game.
    /// </summary>
    [JsonPropertyName("gameextrainfo")]
    public string? GameExtraInfo { get; set; }

    /// <summary>
    /// Gets or sets the application identifier of the currently active game.
    /// </summary>
    [JsonPropertyName("gameid")]
    public string? GameId { get; set; }
}

/// <summary>
/// Root container model for the Steam GetRecentlyPlayedGames Web API response.
/// </summary>
internal class SteamRecentlyPlayedGamesResponse
{
    /// <summary>
    /// Gets or sets the recently played games response container.
    /// </summary>
    [JsonPropertyName("response")]
    public RecentlyPlayedGamesContainer? Response { get; set; }
}

/// <summary>
/// Inner list container holding recently played game items.
/// </summary>
internal class RecentlyPlayedGamesContainer
{
    /// <summary>
    /// Gets or sets the collection of games played in the last two weeks.
    /// </summary>
    [JsonPropertyName("games")]
    public List<RecentlyPlayedGameItem>? Games { get; set; }
}

/// <summary>
/// Represents playtime statistics for a specific game played recently.
/// </summary>
internal class RecentlyPlayedGameItem
{
    /// <summary>
    /// Gets or sets the unique Steam application identifier.
    /// </summary>
    [JsonPropertyName("appid")]
    public uint AppId { get; set; }

    /// <summary>
    /// Gets or sets the title of the game.
    /// </summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>
    /// Gets or sets the total minutes played during the past two weeks.
    /// </summary>
    [JsonPropertyName("playtime_2weeks")]
    public int PlaytimeTwoWeeks { get; set; }

    /// <summary>
    /// Gets or sets total lifetime minutes played across all time.
    /// </summary>
    [JsonPropertyName("playtime_forever")]
    public int PlaytimeForever { get; set; }
}

/// <summary>
/// Root container model for the Steam GetSchemaForGame Web API response.
/// </summary>
internal class SteamSchemaResponse
{
    /// <summary>
    /// Gets or sets game schema metadata container.
    /// </summary>
    [JsonPropertyName("game")]
    public SchemaGameData? Game { get; set; }
}

/// <summary>
/// Holds available game stats definitions for a game schema.
/// </summary>
internal class SchemaGameData
{
    /// <summary>
    /// Gets or sets stats and achievement definitions available for the game.
    /// </summary>
    [JsonPropertyName("availableGameStats")]
    public SchemaStatsData? AvailableGameStats { get; set; }
}

/// <summary>
/// Container list for schema achievement definitions.
/// </summary>
internal class SchemaStatsData
{
    /// <summary>
    /// Gets or sets the list of achievement definitions defined in the game schema.
    /// </summary>
    [JsonPropertyName("achievements")]
    public List<SchemaAchievementItem>? Achievements { get; set; }
}

/// <summary>
/// Represents achievement display definitions including icons and descriptions.
/// </summary>
internal class SchemaAchievementItem
{
    /// <summary>
    /// Gets or sets the internal API identifier matching player stats achievement names.
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the human-readable display title.
    /// </summary>
    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the localized text describing unlock requirements.
    /// </summary>
    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the absolute URL pointing to the unlocked achievement icon graphic.
    /// </summary>
    [JsonPropertyName("icon")]
    public string Icon { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether the achievement is hidden on the community site.
    /// </summary>
    [JsonPropertyName("hidden")]
    [JsonConverter(typeof(FlexibleBoolConverter))]
    public bool Hidden { get; set; }
}

/// <summary>
/// Root container model for the Steam GetPlayerAchievements Web API response.
/// </summary>
internal class SteamPlayerAchievementsResponse
{
    /// <summary>
    /// Gets or sets player stats achievement progress container.
    /// </summary>
    [JsonPropertyName("playerstats")]
    public PlayerStatsData? PlayerStats { get; set; }
}

/// <summary>
/// Inner player statistics payload returned by Steam GetPlayerAchievements.
/// </summary>
internal class PlayerStatsData
{
    /// <summary>
    /// Gets or sets the 64-bit target Steam identifier.
    /// </summary>
    [JsonPropertyName("steamID")]
    public string? SteamId { get; set; }

    /// <summary>
    /// Gets or sets the display name of the target game.
    /// </summary>
    [JsonPropertyName("gameName")]
    public string? GameName { get; set; }

    /// <summary>
    /// Gets or sets the collection of achievement unlock statuses for the user.
    /// </summary>
    [JsonPropertyName("achievements")]
    public List<PlayerAchievementItem>? Achievements { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the request succeeded on Steam's backend.
    /// </summary>
    [JsonPropertyName("success")]
    public bool Success { get; set; } = true;

    /// <summary>
    /// Gets or sets an optional error message string if request processing failed.
    /// </summary>
    [JsonPropertyName("error")]
    public string? Error { get; set; }
}

/// <summary>
/// Root container model for the Steam GetTopAchievementsForGames Web API response.
/// </summary>
internal class SteamTopAchievementsResponse
{
    /// <summary>
    /// Gets or sets the payload containing per-game unlocked achievements.
    /// </summary>
    [JsonPropertyName("response")]
    public SteamTopAchievementsPayload? Response { get; set; }
}

/// <summary>
/// Inner payload returned by Steam GetTopAchievementsForGames.
/// </summary>
internal class SteamTopAchievementsPayload
{
    /// <summary>
    /// Gets or sets the list of games with unlocked achievement details for the requested user.
    /// </summary>
    [JsonPropertyName("games")]
    public List<SteamTopAchievementGame>? Games { get; set; }
}

/// <summary>
/// Per-game unlocked achievement data returned by Steam GetTopAchievementsForGames.
/// </summary>
internal class SteamTopAchievementGame
{
    /// <summary>
    /// Gets or sets the application identifier of the game.
    /// </summary>
    [JsonPropertyName("appid")]
    public uint AppId { get; set; }

    /// <summary>
    /// Gets or sets the total number of achievements defined for the game.
    /// </summary>
    [JsonPropertyName("total_achievements")]
    public int TotalAchievements { get; set; }

    /// <summary>
    /// Gets or sets the achievements the user has unlocked; absent when none are unlocked.
    /// </summary>
    [JsonPropertyName("achievements")]
    public List<SteamTopAchievementItem>? Achievements { get; set; }
}

/// <summary>
/// Individual unlocked achievement entry returned by Steam GetTopAchievementsForGames.
/// </summary>
internal class SteamTopAchievementItem
{
    /// <summary>
    /// Gets or sets the localized display title of the achievement.
    /// </summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>
    /// Gets or sets the localized description of the achievement.
    /// </summary>
    [JsonPropertyName("desc")]
    public string? Desc { get; set; }

    /// <summary>
    /// Gets or sets the unlocked icon file name (without CDN prefix).
    /// </summary>
    [JsonPropertyName("icon")]
    public string? Icon { get; set; }

    /// <summary>
    /// Gets or sets the locked/grayscale icon file name (without CDN prefix).
    /// </summary>
    [JsonPropertyName("icon_gray")]
    public string? IconGray { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the achievement is hidden on the Steam community site.
    /// </summary>
    [JsonPropertyName("hidden")]
    [JsonConverter(typeof(FlexibleBoolConverter))]
    public bool Hidden { get; set; }

    /// <summary>
    /// Gets or sets the global percentage of Steam players who unlocked this achievement, as a numeric string (e.g. "12.0").
    /// </summary>
    [JsonPropertyName("player_percent_unlocked")]
    [JsonConverter(typeof(FlexibleDoubleConverter))]
    public double PercentUnlocked { get; set; }
}

/// <summary>
/// Converts JSON numbers, booleans, or string representations into an integer value.
/// </summary>
internal class FlexibleIntConverter : JsonConverter<int>
{
    /// <summary>
    /// Reads and converts JSON token values to an integer representation.
    /// </summary>
    /// <param name="reader">The JSON reader instance.</param>
    /// <param name="typeToConvert">The target object type.</param>
    /// <param name="options">Serializer options in context.</param>
    /// <returns>An integer representation of the JSON token value.</returns>
    public override int Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Number) return reader.GetInt32();
        if (reader.TokenType == JsonTokenType.True) return 1;
        if (reader.TokenType == JsonTokenType.False) return 0;
        if (reader.TokenType == JsonTokenType.String
            && int.TryParse(reader.GetString(), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int val))
        {
            return val;
        }

        return 0;
    }

    /// <summary>
    /// Writes an integer value to the JSON target stream.
    /// </summary>
    /// <param name="writer">The JSON writer instance.</param>
    /// <param name="value">The integer value to write.</param>
    /// <param name="options">Serializer options in context.</param>
    public override void Write(Utf8JsonWriter writer, int value, JsonSerializerOptions options)
    {
        writer.WriteNumberValue(value);
    }
}

/// <summary>
/// Converts JSON numbers or string representations into a double value.
/// </summary>
internal class FlexibleDoubleConverter : JsonConverter<double>
{
    /// <summary>
    /// Reads and converts JSON token values to a double representation.
    /// </summary>
    /// <param name="reader">The JSON reader instance.</param>
    /// <param name="typeToConvert">The target object type.</param>
    /// <param name="options">Serializer options in context.</param>
    /// <returns>A double representation of the JSON token value.</returns>
    public override double Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Number) return reader.GetDouble();
        if (reader.TokenType == JsonTokenType.String
            && double.TryParse(reader.GetString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double val))
        {
            return val;
        }

        return 0;
    }

    /// <summary>
    /// Writes a double value to the JSON target stream.
    /// </summary>
    /// <param name="writer">The JSON writer instance.</param>
    /// <param name="value">The double value to write.</param>
    /// <param name="options">Serializer options in context.</param>
    public override void Write(Utf8JsonWriter writer, double value, JsonSerializerOptions options)
    {
        writer.WriteNumberValue(value);
    }
}

/// <summary>
/// Converts JSON booleans, numbers, or string representations into a boolean value.
/// </summary>
internal class FlexibleBoolConverter : JsonConverter<bool>
{
    /// <summary>
    /// Reads and converts JSON token values to a boolean representation.
    /// </summary>
    /// <param name="reader">The JSON reader instance.</param>
    /// <param name="typeToConvert">The target object type.</param>
    /// <param name="options">Serializer options in context.</param>
    /// <returns>A boolean representation of the JSON token value.</returns>
    public override bool Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.True) return true;
        if (reader.TokenType == JsonTokenType.False) return false;
        if (reader.TokenType == JsonTokenType.Number) return reader.GetDouble() != 0;
        if (reader.TokenType == JsonTokenType.String)
        {
            var value = reader.GetString();
            if (bool.TryParse(value, out bool parsed)) return parsed;
            if (double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double numeric)) return numeric != 0;
        }

        return false;
    }

    /// <summary>
    /// Writes a boolean value to the JSON target stream.
    /// </summary>
    /// <param name="writer">The JSON writer instance.</param>
    /// <param name="value">The boolean value to write.</param>
    /// <param name="options">Serializer options in context.</param>
    public override void Write(Utf8JsonWriter writer, bool value, JsonSerializerOptions options)
    {
        writer.WriteBooleanValue(value);
    }
}

/// <summary>
/// Converts JSON numbers or string representations into a long integer timestamp.
/// </summary>
internal class FlexibleLongConverter : JsonConverter<long>
{
    /// <summary>
    /// Reads and converts JSON token values to a 64-bit integer timestamp.
    /// </summary>
    /// <param name="reader">The JSON reader instance.</param>
    /// <param name="typeToConvert">The target object type.</param>
    /// <param name="options">Serializer options in context.</param>
    /// <returns>A 64-bit integer representation of the JSON token value.</returns>
    public override long Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Number) return reader.GetInt64();
        if (reader.TokenType == JsonTokenType.String
            && long.TryParse(reader.GetString(), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out long val))
        {
            return val;
        }

        return 0;
    }

    /// <summary>
    /// Writes a 64-bit integer value to the JSON target stream.
    /// </summary>
    /// <param name="writer">The JSON writer instance.</param>
    /// <param name="value">The integer value to write.</param>
    /// <param name="options">Serializer options in context.</param>
    public override void Write(Utf8JsonWriter writer, long value, JsonSerializerOptions options)
    {
        writer.WriteNumberValue(value);
    }
}

/// <summary>
/// Represents individual player achievement progress and unlock timing.
/// </summary>
internal class PlayerAchievementItem
{
    /// <summary>
    /// Gets or sets the internal API identifier of the achievement.
    /// </summary>
    [JsonPropertyName("apiname")]
    public string ApiName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the achievement unlock status (1 for unlocked, 0 for locked).
    /// </summary>
    [JsonPropertyName("achieved")]
    [JsonConverter(typeof(FlexibleIntConverter))]
    public int Achieved { get; set; }

    /// <summary>
    /// Gets or sets the Unix epoch timestamp indicating when the achievement was unlocked.
    /// </summary>
    [JsonPropertyName("unlocktime")]
    [JsonConverter(typeof(FlexibleLongConverter))]
    public long UnlockTime { get; set; }

    /// <summary>
    /// Gets or sets the optional display name returned by the player stats endpoint.
    /// </summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>
    /// Gets or sets the optional description returned by the player stats endpoint.
    /// </summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>
    /// Gets a value indicating whether the achievement has been unlocked by the player.
    /// </summary>
    [JsonIgnore]
    public bool IsAchieved => Achieved == 1;
}

#endregion