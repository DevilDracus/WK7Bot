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
    /// Tracks one in-flight Steam fetch per Steam ID so concurrent callers (presence event + periodic
    /// refresh tick) share a single request instead of stampeding the Steam Web API.
    /// </summary>
    private readonly ConcurrentDictionary<string, Task<SteamUserData?>> _inFlight = new();

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
    public Task<SteamUserData?> GetSteamUserDataAsync(string steamId, CancellationToken cancellationToken = default)
        => GetSteamUserDataAsync(steamId, forceRefresh: false, cancellationToken);

    /// <summary>
    /// Returns cached Steam user data when younger than the time-to-live, or fetches fresh data when the entry
    /// expired or when <paramref name="forceRefresh"/> is set. Concurrent cache misses for the same Steam ID
    /// share one in-flight Steam request.
    /// </summary>
    /// <param name="steamId">The 64-bit Steam ID of the user.</param>
    /// <param name="forceRefresh">When true, a still-fresh cached entry is bypassed and Steam is queried again.</param>
    /// <param name="cancellationToken">A cancellation token to monitor for cancellation requests.</param>
    /// <returns>The cached or freshly fetched <see cref="SteamUserData"/>, or null when retrieval fails.</returns>
    public async Task<SteamUserData?> GetSteamUserDataAsync(string steamId, bool forceRefresh, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(steamId);

        var now = DateTimeOffset.UtcNow;
        if (!forceRefresh && _entries.TryGetValue(steamId, out var entry) && now - entry.FetchedAt < _timeToLive)
        {
            return entry.Data;
        }

        // The caller whose factory created the task owns its eviction and runs it only after the
        // fetch has settled. Waiters must never remove the shared task: a waiter that cancels would
        // otherwise evict a still-running fetch and defeat the single-flight guarantee.
        var isOwner = false;
        var fetchTask = _inFlight.GetOrAdd(steamId, id =>
        {
            isOwner = true;
            return FetchAndStoreAsync(id, cancellationToken);
        });

        if (!isOwner)
        {
            return await fetchTask.WaitAsync(cancellationToken);
        }

        try
        {
            return await fetchTask;
        }
        finally
        {
            ((ICollection<KeyValuePair<string, Task<SteamUserData?>>>)_inFlight)
                .Remove(new KeyValuePair<string, Task<SteamUserData?>>(steamId, fetchTask));
        }
    }

    /// <summary>
    /// Fetches fresh Steam user data and stores it (including null results) in the cache.
    /// </summary>
    /// <param name="steamId">The 64-bit Steam ID of the user.</param>
    /// <param name="cancellationToken">A cancellation token to monitor for cancellation requests.</param>
    /// <returns>The freshly fetched <see cref="SteamUserData"/>, or null when retrieval fails.</returns>
    private async Task<SteamUserData?> FetchAndStoreAsync(string steamId, CancellationToken cancellationToken)
    {
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
