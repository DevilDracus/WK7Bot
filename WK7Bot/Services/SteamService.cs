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
using WK7Bot.Models.Steam;
using WK7Bot.Models;
using WK7Bot.Options;
using WK7Bot.Services.Interfaces;

namespace WK7Bot.Services;

/// <summary>
/// Communicates with Valve Steam Web API endpoints to fetch player profiles, recent activity, and enriched achievement metrics.
/// </summary>
public partial class SteamService : ISteamService
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

            // Await the tasks again instead of reading .Result: Task.Result would wrap a failure in
            // an AggregateException and bypass the OperationCanceledException filter below.
            var schemaMap = await schemaTask;
            var playerAchievements = await playerAchievementsTask;

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
    /// Converts numeric Steam persona state values to human-readable status labels.
    /// </summary>
    /// <param name="stateCode">The integer status code returned by the Steam API.</param>
    /// <returns>A string representation of the user's online state.</returns>
    internal static string MapPersonaState(int stateCode)
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
