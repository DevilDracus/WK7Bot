namespace WK7Bot.Core.Utilities;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Short-lived key cache that collapses concurrent misses for the same key into a single fetch
/// ("single flight"), so rapid repeated lookups hit the upstream service once instead of stampeding
/// it. Failures are cached as null results and expire like any other entry.
/// </summary>
/// <typeparam name="TValue">The cached value type.</typeparam>
public abstract class SingleFlightCache<TValue>
{
    private readonly TimeSpan _timeToLive;
    private readonly ConcurrentDictionary<string, CacheEntry> _entries = new();
    private readonly ConcurrentDictionary<string, Task<TValue?>> _inFlight = new();
    private readonly object _inFlightGate = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="SingleFlightCache{TValue}"/> class.
    /// </summary>
    /// <param name="timeToLive">How long a cached entry remains valid.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="timeToLive"/> is not positive.</exception>
    protected SingleFlightCache(TimeSpan timeToLive)
    {
        if (timeToLive <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeToLive), timeToLive, "Time to live must be positive.");
        }

        _timeToLive = timeToLive;
    }

    /// <summary>
    /// Returns the cached value when younger than the time-to-live; otherwise fetches fresh data.
    /// Concurrent misses for the same key share one fetch.
    /// </summary>
    /// <param name="key">The cache key identifying the upstream entity.</param>
    /// <param name="cancellationToken">A cancellation token to monitor for cancellation requests.</param>
    /// <returns>The cached or freshly fetched value, or null when retrieval failed.</returns>
    public Task<TValue?> GetAsync(string key, CancellationToken cancellationToken = default)
        => GetAsync(key, forceRefresh: false, cancellationToken);

    /// <summary>
    /// Returns the cached value when younger than the time-to-live, or fetches fresh data when the
    /// entry expired or when <paramref name="forceRefresh"/> is set. Concurrent misses for the same
    /// key share one fetch.
    /// </summary>
    /// <param name="key">The cache key identifying the upstream entity.</param>
    /// <param name="forceRefresh">When true, a still-fresh cached entry is bypassed and the value is queried again.</param>
    /// <param name="cancellationToken">A cancellation token to monitor for cancellation requests.</param>
    /// <returns>The cached or freshly fetched value, or null when retrieval failed.</returns>
    public async Task<TValue?> GetAsync(string key, bool forceRefresh, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var now = DateTimeOffset.UtcNow;
        if (!forceRefresh && _entries.TryGetValue(key, out var entry) && now - entry.FetchedAt < _timeToLive)
        {
            return entry.Data;
        }

        var fetchTask = GetOrStartFetchAsync(key, cancellationToken);
        return await fetchTask.WaitAsync(cancellationToken);
    }

    /// <summary>
    /// Fetches the value for a cache miss. Implementations return null (instead of throwing) when
    /// the upstream read failed, so the failure is cached for the remaining time-to-live.
    /// </summary>
    /// <param name="key">The cache key identifying the upstream entity.</param>
    /// <param name="cancellationToken">A cancellation token to monitor for cancellation requests.</param>
    /// <returns>The fetched value, or null when the read failed.</returns>
    protected abstract Task<TValue?> FetchAsync(string key, CancellationToken cancellationToken);

    /// <summary>
    /// Returns the in-flight fetch for <paramref name="key"/>, starting one under the gate when none
    /// is running. Creation under the gate matters: ConcurrentDictionary.GetOrAdd may invoke its
    /// factory on more than one thread, which would start a second request for the same key.
    /// The shared fetch follows the initiating caller's token; every other caller only waits.
    /// </summary>
    private Task<TValue?> GetOrStartFetchAsync(string key, CancellationToken cancellationToken)
    {
        lock (_inFlightGate)
        {
            if (_inFlight.TryGetValue(key, out var existing))
            {
                return existing;
            }

            var fetch = FetchAndStoreAsync(key, cancellationToken);
            _inFlight[key] = fetch;

            // Attached after storing, so a fetch that completed synchronously still evicts itself.
            // The entry disappears exactly when the shared fetch settles — never when a waiting
            // caller cancels its own token — which keeps the single-flight guarantee intact.
            _ = fetch.ContinueWith(_ => EvictInFlight(key, fetch));

            return fetch;
        }
    }

    /// <summary>
    /// Fetches fresh data and stores it (including a null result from a failed fetch) in the cache.
    /// </summary>
    private async Task<TValue?> FetchAndStoreAsync(string key, CancellationToken cancellationToken)
    {
        var data = await FetchAsync(key, cancellationToken);
        _entries[key] = new CacheEntry(data, DateTimeOffset.UtcNow);
        return data;
    }

    /// <summary>
    /// Removes an in-flight entry once its fetch has settled; removes nothing when a newer fetch has
    /// already replaced it.
    /// </summary>
    private void EvictInFlight(string key, Task<TValue?> fetch)
    {
        ((ICollection<KeyValuePair<string, Task<TValue?>>>)_inFlight)
            .Remove(new KeyValuePair<string, Task<TValue?>>(key, fetch));
    }

    /// <summary>
    /// Immutable cache entry holding the fetched data together with its fetch timestamp.
    /// </summary>
    /// <param name="Data">The cached value, possibly null when the fetch failed.</param>
    /// <param name="FetchedAt">The UTC timestamp when the entry was stored.</param>
    private sealed record CacheEntry(TValue? Data, DateTimeOffset FetchedAt);
}
