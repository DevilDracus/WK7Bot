using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using WK7Bot.Models;
using WK7Bot.Services.Interfaces;

namespace WK7Bot.Services;

/// <summary>
/// Caches <see cref="BattleNetUserData"/> lookups per refresh token for a short time-to-live so rapid Discord presence
/// update storms do not hammer the Battle.net API with duplicate requests.
/// </summary>
public class BattleNetDataCache
{
    private readonly IBattleNetService _battleNetService;
    private readonly TimeSpan _timeToLive;
    private readonly ConcurrentDictionary<string, CacheEntry> _entries = new();

    /// <summary>
    /// Tracks one in-flight Battle.net fetch per refresh token so concurrent callers (presence event +
    /// periodic refresh tick) share a single request instead of stampeding the Battle.net API.
    /// </summary>
    private readonly ConcurrentDictionary<string, Task<BattleNetUserData?>> _inFlight = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="BattleNetDataCache"/> class.
    /// </summary>
    /// <param name="battleNetService">The underlying Battle.net service used on cache misses.</param>
    /// <param name="timeToLive">How long a cached entry remains valid.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="battleNetService"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="timeToLive"/> is not positive.</exception>
    public BattleNetDataCache(IBattleNetService battleNetService, TimeSpan timeToLive)
    {
        _battleNetService = battleNetService ?? throw new ArgumentNullException(nameof(battleNetService));
        if (timeToLive <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeToLive), timeToLive, "Time to live must be positive.");
        }

        _timeToLive = timeToLive;
    }

    /// <summary>
    /// Returns cached Battle.net user data when younger than the time-to-live; otherwise fetches fresh data from the Battle.net service.
    /// </summary>
    /// <param name="refreshToken">The OAuth refresh token of the mapped Battle.net account.</param>
    /// <param name="cancellationToken">A cancellation token to monitor for cancellation requests.</param>
    /// <returns>The cached or freshly fetched <see cref="BattleNetUserData"/>, or null when retrieval fails.</returns>
    public Task<BattleNetUserData?> GetBattleNetUserDataAsync(string refreshToken, CancellationToken cancellationToken = default)
        => GetBattleNetUserDataAsync(refreshToken, forceRefresh: false, cancellationToken);

    /// <summary>
    /// Returns cached Battle.net user data when younger than the time-to-live, or fetches fresh data when the
    /// entry expired or when <paramref name="forceRefresh"/> is set. Concurrent cache misses for the same
    /// refresh token share one in-flight Battle.net request.
    /// </summary>
    /// <param name="refreshToken">The OAuth refresh token of the mapped Battle.net account.</param>
    /// <param name="forceRefresh">When true, a still-fresh cached entry is bypassed and Battle.net is queried again.</param>
    /// <param name="cancellationToken">A cancellation token to monitor for cancellation requests.</param>
    /// <returns>The cached or freshly fetched <see cref="BattleNetUserData"/>, or null when retrieval fails.</returns>
    public async Task<BattleNetUserData?> GetBattleNetUserDataAsync(string refreshToken, bool forceRefresh, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(refreshToken);

        var now = DateTimeOffset.UtcNow;
        if (!forceRefresh && _entries.TryGetValue(refreshToken, out var entry) && now - entry.FetchedAt < _timeToLive)
        {
            return entry.Data;
        }

        // The caller whose factory created the task owns its eviction and runs it only after the
        // fetch has settled. Waiters must never remove the shared task: a waiter that cancels would
        // otherwise evict a still-running fetch and defeat the single-flight guarantee.
        var isOwner = false;
        var fetchTask = _inFlight.GetOrAdd(refreshToken, token =>
        {
            isOwner = true;
            return FetchAndStoreAsync(token, cancellationToken);
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
            ((ICollection<KeyValuePair<string, Task<BattleNetUserData?>>>)_inFlight)
                .Remove(new KeyValuePair<string, Task<BattleNetUserData?>>(refreshToken, fetchTask));
        }
    }

    /// <summary>
    /// Fetches fresh Battle.net user data and stores it (including null results) in the cache.
    /// </summary>
    /// <param name="refreshToken">The OAuth refresh token of the mapped Battle.net account.</param>
    /// <param name="cancellationToken">A cancellation token to monitor for cancellation requests.</param>
    /// <returns>The freshly fetched <see cref="BattleNetUserData"/>, or null when retrieval fails.</returns>
    private async Task<BattleNetUserData?> FetchAndStoreAsync(string refreshToken, CancellationToken cancellationToken)
    {
        var data = await _battleNetService.GetBattleNetUserDataAsync(refreshToken, cancellationToken);
        _entries[refreshToken] = new CacheEntry(data, DateTimeOffset.UtcNow);
        return data;
    }

    /// <summary>
    /// Immutable cache entry holding the fetched data together with its fetch timestamp.
    /// </summary>
    /// <param name="Data">The cached Battle.net user data, possibly null when the fetch failed.</param>
    /// <param name="FetchedAt">The UTC timestamp when the entry was stored.</param>
    private sealed record CacheEntry(BattleNetUserData? Data, DateTimeOffset FetchedAt);
}
