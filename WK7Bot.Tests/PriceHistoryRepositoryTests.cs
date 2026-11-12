using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using WK7Bot.Core.Entities;
using WK7Bot.Core.Interfaces;
using WK7Bot.Infrastructure.Data;
using Xunit;

namespace WK7Bot.Tests;

public class PriceHistoryRepositoryTests : IDisposable
{
    private readonly IDbContextFactory<BotDbContext> _factory;
    private readonly IPriceHistoryRepository _repository;

    public PriceHistoryRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<BotDbContext>()
            .UseInMemoryDatabase(databaseName: $"price-history-tests-{Guid.NewGuid()}")
            .Options;

        _factory = new PooledDbContextFactory<BotDbContext>(options);
        _repository = new PriceHistoryRepository(_factory);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task AddSnapshotsAsync_ThenGetSnapshotsAsync_ReturnsInOrder()
    {
        var now = DateTime.UtcNow;
        await _repository.AddSnapshotsAsync(new[]
        {
            new PriceHistorySnapshot
            {
                AppId = 123,
                Name = "Game",
                Store = "Steam",
                Price = 29.99m,
                RegularPrice = 59.99m,
                DiscountPercent = 50,
                RecordedAt = now.AddDays(-2)
            },
            new PriceHistorySnapshot
            {
                AppId = 123,
                Name = "Game",
                Store = "Steam",
                Price = 19.99m,
                RegularPrice = 59.99m,
                DiscountPercent = 67,
                RecordedAt = now
            }
        });

        var results = await _repository.GetSnapshotsAsync(123, now.AddDays(-7));

        Assert.Equal(2, results.Count);
        Assert.Equal(50, results[0].DiscountPercent);
        Assert.Equal(67, results[1].DiscountPercent);
    }

    [Fact]
    public async Task AddSnapshotsAsync_DeduplicatesSameHour()
    {
        // Anchor to a time safely inside an hour so the two snapshots (5 min apart)
        // cannot straddle an hour boundary and make the test time-dependent/flaky.
        var now = new DateTime(2026, 11, 12, 10, 30, 0, DateTimeKind.Utc);
        var inserted = await _repository.AddSnapshotsAsync(new[]
        {
            new PriceHistorySnapshot { AppId = 1, Store = "Steam", Price = 10m, RecordedAt = now },
            new PriceHistorySnapshot { AppId = 1, Store = "Steam", Price = 12m, RecordedAt = now.AddMinutes(5) }
        });

        Assert.Equal(1, inserted);
        var results = await _repository.GetSnapshotsAsync(1, now.AddDays(-1));
        Assert.Single(results);
    }

    [Fact]
    public async Task PruneAsync_RemovesOldSnapshots()
    {
        var now = DateTime.UtcNow;
        await _repository.AddSnapshotsAsync(new[]
        {
            new PriceHistorySnapshot { AppId = 1, Store = "Steam", Price = 10m, RecordedAt = now.AddDays(-40) },
            new PriceHistorySnapshot { AppId = 1, Store = "Steam", Price = 10m, RecordedAt = now }
        });

        var removed = await _repository.PruneAsync(now.AddDays(-30));

        Assert.Equal(1, removed);
        var remaining = await _repository.GetSnapshotsAsync(1, now.AddDays(-45));
        Assert.Single(remaining);
    }
}
