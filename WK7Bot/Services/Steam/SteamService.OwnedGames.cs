namespace WK7Bot.Services;

using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using WK7Bot.Models.Steam;
using WK7Bot.Options;

public partial class SteamService
{
    /// <summary>
    /// Retrieves the list of games owned by the specified Steam user via IPlayerService/GetOwnedGames.
    /// </summary>
    /// <param name="steamId">The 64-bit Steam identifier of the user.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A collection of owned games as (AppId, Name) tuples.</returns>
    public async Task<IReadOnlyList<(uint AppId, string Name)>> GetOwnedGamesAsync(
        string steamId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(steamId);

        if (string.IsNullOrWhiteSpace(_options.SteamApiKey))
        {
            _logger.LogWarning("Steam API key is missing in configuration. Skipping owned games retrieval.");
            return Array.Empty<(uint, string)>();
        }

        try
        {
            string url = $"https://api.steampowered.com/IPlayerService/GetOwnedGames/v0001/?key={_options.SteamApiKey}&steamid={steamId}&include_appinfo=true&format=json";

            using var response = await GetSteamApiWithRetryAsync(url, "GetOwnedGames", cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Steam API returned status code {StatusCode} for GetOwnedGames (SteamId: {SteamId}).",
                    response.StatusCode,
                    steamId);
                return Array.Empty<(uint, string)>();
            }

            var result = await response.Content.ReadFromJsonAsync<SteamOwnedGamesResponse>(cancellationToken: cancellationToken).ConfigureAwait(false);
            var games = result?.Response?.Games;
            if (games == null || games.Count == 0)
            {
                return Array.Empty<(uint, string)>();
            }

            return games
                .Where(g => g.AppId > 0)
                .Select(g => (g.AppId, g.Name ?? string.Empty))
                .ToList();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Owned games fetch timed out for Steam ID {SteamId}.", steamId);
            return Array.Empty<(uint, string)>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred while fetching owned games for Steam ID {SteamId}.", steamId);
            return Array.Empty<(uint, string)>();
        }
    }
}
