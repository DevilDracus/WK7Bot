namespace WK7Bot.Infrastructure.Data;

using Microsoft.EntityFrameworkCore;
using WK7Bot.Core.Entities;
using WK7Bot.Core.Interfaces;

/// <summary>
/// SQLite Entity Framework implementation of <see cref="IPriceHistoryRepository"/>.
/// </summary>
public class PriceHistoryRepository : IPriceHistoryRepository
{
    private readonly IDbContextFactory<BotDbContext> _factory;

    // Serializes snapshot inserts so concurrent callers cannot race on the hourly dedup bucket.
    private static readonly SemaphoreSlim AddSnapshotsLock = new(1, 1);

    /// <summary>Initializes a new instance of the <see cref="PriceHistoryRepository"/> class.</summary>
    public PriceHistoryRepository(IDbContextFactory<BotDbContext> factory)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    }

    /// <inheritdoc />
    public async Task<int> AddSnapshotsAsync(IEnumerable<PriceHistorySnapshot> snapshots, CancellationToken cancellationToken = default)
    {
        await AddSnapshotsLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var list = snapshots?.ToList() ?? [];
            if (list.Count == 0)
            {
                return 0;
            }

            await using var context = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

            // Deduplicate within the same hour and store to avoid runaway growth. In-memory filtering
            // works across SQLite and EF InMemory providers; database-backed queries cannot see pending adds.
            var appIds = list.Select(x => x.AppId).Distinct().ToList();
            var existing = await context.PriceHistorySnapshots
                .Where(s => appIds.Contains(s.AppId))
                .Select(s => new { s.AppId, s.Store, Hour = new DateTime(s.RecordedAt.Year, s.RecordedAt.Month, s.RecordedAt.Day, s.RecordedAt.Hour, 0, 0, DateTimeKind.Utc) })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var seen = existing.ToHashSet();
            var inserted = 0;
            foreach (var snapshot in list)
            {
                var bucket = new DateTime(snapshot.RecordedAt.Year, snapshot.RecordedAt.Month, snapshot.RecordedAt.Day, snapshot.RecordedAt.Hour, 0, 0, DateTimeKind.Utc);
                var key = new { snapshot.AppId, snapshot.Store, Hour = bucket };
                if (seen.Add(key))
                {
                    context.PriceHistorySnapshots.Add(snapshot);
                    inserted++;
                }
            }

            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return inserted;
        }
        finally
        {
            AddSnapshotsLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PriceHistorySnapshot>> GetSnapshotsAsync(
        uint appId,
        DateTime since,
        string? store = null,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var query = context.PriceHistorySnapshots
            .Where(s => s.AppId == appId && s.RecordedAt >= since);

        if (!string.IsNullOrWhiteSpace(store))
        {
            query = query.Where(s => s.Store == store);
        }

        return await query
            .OrderBy(s => s.RecordedAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<int> PruneAsync(DateTime olderThan, CancellationToken cancellationToken = default)
    {
        await using var context = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var stale = await context.PriceHistorySnapshots
            .Where(s => s.RecordedAt < olderThan)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (stale.Count > 0)
        {
            context.PriceHistorySnapshots.RemoveRange(stale);
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return stale.Count;
    }
}
