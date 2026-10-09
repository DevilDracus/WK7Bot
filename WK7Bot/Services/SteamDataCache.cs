namespace WK7Bot.Services;

using System;
using System.Threading;
using System.Threading.Tasks;
using WK7Bot.Core.Utilities;
using WK7Bot.Models;
using WK7Bot.Services.Interfaces;

/// <summary>
/// Caches <see cref="SteamUserData"/> lookups per Steam ID for a short time-to-live so rapid Discord
/// presence update storms do not hammer the Steam Web API with duplicate requests. Concurrent misses
/// for the same user share a single request (see <see cref="SingleFlightCache{TValue}"/>).
/// </summary>
public class SteamDataCache : SingleFlightCache<SteamUserData>
{
    private readonly ISteamService _steamService;

    /// <summary>
    /// Initializes a new instance of the <see cref="SteamDataCache"/> class.
    /// </summary>
    /// <param name="steamService">The underlying Steam service used on cache misses.</param>
    /// <param name="timeToLive">How long a cached entry remains valid.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="steamService"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="timeToLive"/> is not positive.</exception>
    public SteamDataCache(ISteamService steamService, TimeSpan timeToLive)
        : base(timeToLive)
    {
        _steamService = steamService ?? throw new ArgumentNullException(nameof(steamService));
    }

    /// <summary>
    /// Returns cached Steam user data when younger than the time-to-live; otherwise fetches fresh data from the Steam service.
    /// </summary>
    /// <param name="steamId">The 64-bit Steam ID of the user.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>The cached or freshly fetched <see cref="SteamUserData"/>, or null when retrieval fails.</returns>
    public Task<SteamUserData?> GetSteamUserDataAsync(string steamId, CancellationToken cancellationToken = default)
        => GetAsync(steamId, cancellationToken);

    /// <summary>
    /// Returns cached Steam user data when younger than the time-to-live, or fetches fresh data when the entry
    /// expired or when <paramref name="forceRefresh"/> is set. Concurrent cache misses for the same Steam ID
    /// share one in-flight Steam request.
    /// </summary>
    /// <param name="steamId">The 64-bit Steam ID of the user.</param>
    /// <param name="forceRefresh">When true, a still-fresh cached entry is bypassed and Steam is queried again.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>The cached or freshly fetched <see cref="SteamUserData"/>, or null when retrieval fails.</returns>
    public Task<SteamUserData?> GetSteamUserDataAsync(string steamId, bool forceRefresh, CancellationToken cancellationToken = default)
        => GetAsync(steamId, forceRefresh, cancellationToken);

    /// <inheritdoc/>
    protected override Task<SteamUserData?> FetchAsync(string steamId, CancellationToken cancellationToken)
        => _steamService.GetSteamUserDataAsync(steamId, cancellationToken);
}
