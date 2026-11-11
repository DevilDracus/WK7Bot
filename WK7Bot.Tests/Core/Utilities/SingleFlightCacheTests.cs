using System.Collections.Concurrent;
using WK7Bot.Core.Utilities;
using Xunit;

namespace WK7Bot.Tests;

public class SingleFlightCacheTests
{
    private sealed class FakeCache : SingleFlightCache<int?>
    {
        private readonly Func<string, CancellationToken, Task<int?>> _fetch;

        public FakeCache(TimeSpan timeToLive, int maxEntries = 512, Func<string, CancellationToken, Task<int?>>? fetch = null)
            : base(timeToLive, maxEntries)
        {
            _fetch = fetch ?? ((_, _) => Task.FromResult<int?>(42));
        }

        protected override Task<int?> FetchAsync(string key, CancellationToken cancellationToken)
            => _fetch(key, cancellationToken);
    }

    [Fact]
    public async Task GetAsync_CachesValueWithinTtl()
    {
        var cache = new FakeCache(TimeSpan.FromMinutes(1));

        var first = await cache.GetAsync("k");
        var second = await cache.GetAsync("k");

        Assert.Equal(42, first);
        Assert.Equal(42, second);
    }

    [Fact]
    public async Task GetAsync_RefetchesAfterTtlExpires()
    {
        var callCount = 0;
        var cache = new FakeCache(TimeSpan.FromMilliseconds(50), fetch: (_, _) =>
        {
            Interlocked.Increment(ref callCount);
            return Task.FromResult<int?>(callCount);
        });

        var first = await cache.GetAsync("k");
        await Task.Delay(100);
        var second = await cache.GetAsync("k");

        Assert.Equal(1, first);
        Assert.Equal(2, second);
    }

    [Fact]
    public async Task GetAsync_CachesNullResult()
    {
        var callCount = 0;
        var cache = new FakeCache(TimeSpan.FromMinutes(1), fetch: (_, _) =>
        {
            Interlocked.Increment(ref callCount);
            return Task.FromResult<int?>(null);
        });

        var first = await cache.GetAsync("k");
        var second = await cache.GetAsync("k");

        Assert.Null(first);
        Assert.Null(second);
        Assert.Equal(1, callCount);
    }

    [Fact]
    public async Task GetAsync_CollapsesConcurrentMissesIntoSingleFetch()
    {
        var callCount = 0;
        var fetchStarted = new TaskCompletionSource();
        var cache = new FakeCache(TimeSpan.FromMinutes(1), fetch: async (key, _) =>
        {
            Interlocked.Increment(ref callCount);
            fetchStarted.SetResult();
            await Task.Delay(50);
            return callCount;
        });

        var t1 = cache.GetAsync("k");
        var t2 = cache.GetAsync("k");

        await fetchStarted.Task;
        var results = await Task.WhenAll(t1, t2);

        Assert.Equal(1, callCount);
        Assert.Equal(results[0], results[1]);
    }

    [Fact]
    public async Task GetAsync_CancellationOfOneWaiterDoesNotCancelSharedFetch()
    {
        var callCount = 0;
        var fetchStarted = new TaskCompletionSource();
        var cache = new FakeCache(TimeSpan.FromMinutes(1), fetch: async (_, ct) =>
        {
            Interlocked.Increment(ref callCount);
            fetchStarted.SetResult();
            await Task.Delay(100, ct);
            return callCount;
        });

        var cts = new CancellationTokenSource();
        var waitingTask = cache.GetAsync("k", cts.Token);

        await fetchStarted.Task;
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waitingTask);

        var otherResult = await cache.GetAsync("k");
        Assert.Equal(1, callCount);
        Assert.Equal(1, otherResult);
    }

    [Fact]
    public async Task GetAsync_EnforcesMaxEntries()
    {
        var fetchCounts = new ConcurrentDictionary<string, int>();
        var cache = new FakeCache(TimeSpan.FromMinutes(1), maxEntries: 2, fetch: async (key, _) =>
        {
            fetchCounts.AddOrUpdate(key, 1, (_, c) => c + 1);
            await Task.Delay(10);
            return fetchCounts[key];
        });

        await cache.GetAsync("a");
        await cache.GetAsync("b");
        await cache.GetAsync("c");

        // The oldest entry should have been evicted, so a re-fetch is required.
        await cache.GetAsync("a");

        Assert.True(fetchCounts["a"] >= 2, "Oldest entry 'a' should have been evicted and refetched.");
        Assert.Equal(1, fetchCounts["b"]);
        Assert.Equal(1, fetchCounts["c"]);
    }

    [Fact]
    public void Constructor_Throws_WhenTimeToLiveIsNotPositive()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new FakeCache(TimeSpan.Zero));
    }

    [Fact]
    public void Constructor_Throws_WhenMaxEntriesIsNotPositive()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new FakeCache(TimeSpan.FromMinutes(1), 0));
    }
}
