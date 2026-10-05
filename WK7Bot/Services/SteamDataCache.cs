using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using WK7Bot.Models;
using WK7Bot.Services.Interfaces;

namespace WK7Bot.Services;

/// <summary>
/// Caches <see cref="SteamUserData"/> lookups per Steam ID for a short time-to-live so rapid Discord presence
/// update storms do not hammer the Steam Web API with duplicate requests.
/// </summary>
public class SteamDataCache
{
    private readonly ISteamService _steamService;
    private readonly TimeSpan _timeToLive;
    private readonly ConcurrentDictionary<string, CacheEntry> _entries = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="SteamDataCache"/> class.
    /// </summary>
    /// <param name="steamService">The underlying Steam service used on cache misses.</param>
    /// <param name="timeToLive">How long a cached entry remains valid.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="steamService"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="timeToLive"/> is not positive.</exception>
    public SteamDataCache(ISteamService steamService, TimeSpan timeToLive)
    {
        _steamService = steamService ?? throw new ArgumentNullException(nameof(steamService));
        if (timeToLive <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeToLive), timeToLive, "Time to live must be positive.");
        }

        _timeToLive = timeToLive;
    }

    /// <summary>
    /// Returns cached Steam user data when younger than the time-to-live; otherwise fetches fresh data from the Steam service.
    /// </summary>
    /// <param name="steamId">The 64-bit Steam ID of the user.</param>
    /// <param name="cancellationToken">A cancellation token to monitor for cancellation requests.</param>
    /// <returns>The cached or freshly fetched <see cref="SteamUserData"/>, or null when retrieval fails.</returns>
    public async Task<SteamUserData?> GetSteamUserDataAsync(string steamId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(steamId);

        var now = DateTimeOffset.UtcNow;
        if (_entries.TryGetValue(steamId, out var entry) && now - entry.FetchedAt < _timeToLive)
        {
            return entry.Data;
        }

        var data = await _steamService.GetSteamUserDataAsync(steamId, cancellationToken);
        _entries[steamId] = new CacheEntry(data, DateTimeOffset.UtcNow);
        return data;
    }

    /// <summary>
    /// Immutable cache entry holding the fetched data together with its fetch timestamp.
    /// </summary>
    /// <param name="Data">The cached Steam user data, possibly null when the fetch failed.</param>
    /// <param name="FetchedAt">The UTC timestamp when the entry was stored.</param>
    private sealed record CacheEntry(SteamUserData? Data, DateTimeOffset FetchedAt);
}
