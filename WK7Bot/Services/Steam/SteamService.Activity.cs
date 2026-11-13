namespace WK7Bot.Services;

using System.Net.Http.Json;
using WK7Bot.Models;
using WK7Bot.Models.Steam;

public partial class SteamService
{
    /// <summary>
    /// Fetches lightweight activity summaries for multiple Steam IDs in a single batch.
    /// </summary>
    /// <param name="steamIds">The 64-bit Steam IDs to query.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A collection of <see cref="SteamUserActivity"/> snapshots containing current and recent game activity.</returns>
    public async Task<IReadOnlyList<SteamUserActivity>> GetUserActivitiesAsync(
        IEnumerable<string> steamIds,
        CancellationToken cancellationToken = default)
    {
        var ids = steamIds?.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (ids == null || ids.Count == 0)
        {
            return Array.Empty<SteamUserActivity>();
        }

        if (string.IsNullOrWhiteSpace(_options.SteamApiKey))
        {
            _logger.LogWarning("Steam API key is missing in configuration. Skipping Steam user activities retrieval.");
            return Array.Empty<SteamUserActivity>();
        }

        var summaries = await FetchPlayerSummariesBatchAsync(ids, cancellationToken).ConfigureAwait(false);
        if (summaries.Count == 0)
        {
            return Array.Empty<SteamUserActivity>();
        }

        var activities = summaries.Select(ToActivity).ToList();
        await EnrichRecentGamesBatchAsync(activities, cancellationToken).ConfigureAwait(false);
        return activities;
    }

    private async Task<List<PlayerSummaryItem>> FetchPlayerSummariesBatchAsync(
        List<string> steamIds,
        CancellationToken cancellationToken)
    {
        var joinedIds = string.Join(",", steamIds);
        var url = $"https://api.steampowered.com/ISteamUser/GetPlayerSummaries/v0002/?key={_options.SteamApiKey}&steamids={joinedIds}";

        try
        {
            using var response = await GetSteamApiWithRetryAsync(url, "GetPlayerSummariesBatch", cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Steam API returned status code {StatusCode} for batch GetPlayerSummaries.",
                    response.StatusCode);
                return [];
            }

            var result = await response.Content.ReadFromJsonAsync<SteamPlayerSummariesResponse>(cancellationToken: cancellationToken);
            return result?.Response?.Players ?? [];
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Batch GetPlayerSummaries request failed.");
            return [];
        }
    }

    /// <summary>Maps a player summary to a Steam user activity.</summary>
    /// <param name="player">The player summary.</param>
    /// <returns>The activity.</returns>
    private static SteamUserActivity ToActivity(PlayerSummaryItem player)
    {
        var hasCurrentGame = !string.IsNullOrWhiteSpace(player.GameExtraInfo);
        uint? gameAppId = null;
        if (hasCurrentGame && uint.TryParse(player.GameId ?? string.Empty, out var parsedAppId))
        {
            gameAppId = parsedAppId;
        }

        return new SteamUserActivity
        {
            SteamId = player.SteamId ?? string.Empty,
            PersonaName = player.PersonaName ?? string.Empty,
            PersonaState = MapPersonaState(player.PersonaState),
            SteamAvatarUrl = string.IsNullOrWhiteSpace(player.AvatarFull) ? null : player.AvatarFull,
            CurrentGameTitle = hasCurrentGame ? player.GameExtraInfo : null,
            CurrentGameAppId = gameAppId
        };
    }

    private async Task EnrichRecentGamesBatchAsync(
        List<SteamUserActivity> activities,
        CancellationToken cancellationToken)
    {
        var fetchTasks = activities.Select(a => EnrichRecentGamesAsync(a, cancellationToken));
        await Task.WhenAll(fetchTasks).ConfigureAwait(false);
    }

    /// <summary>Enriches an activity with recent game details.</summary>
    /// <param name="activity">The activity.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task tracking the operation.</returns>
    private async Task EnrichRecentGamesAsync(SteamUserActivity activity, CancellationToken cancellationToken)
    {
        var url = $"https://api.steampowered.com/IPlayerService/GetRecentlyPlayedGames/v0001/?key={_options.SteamApiKey}&steamid={activity.SteamId}&format=json";

        try
        {
            using var response = await GetSteamApiWithRetryAsync(url, "GetRecentlyPlayedGamesBatch", cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return;
            }

            var result = await response.Content.ReadFromJsonAsync<SteamRecentlyPlayedGamesResponse>(cancellationToken: cancellationToken);
            var games = result?.Response?.Games;
            if (games == null || games.Count == 0)
            {
                return;
            }

            activity.RecentGames = games.Select(g => new SteamRecentGame
            {
                AppId = g.AppId,
                Name = g.Name ?? string.Empty,
                PlaytimeTwoWeeksMinutes = g.PlaytimeTwoWeeks,
                PlaytimeForeverMinutes = g.PlaytimeForever
            }).ToList();
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Failed to fetch recently played games for Steam ID {SteamId}.", activity.SteamId);
        }
    }
}
