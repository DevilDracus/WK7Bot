using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using WK7Bot.Core.Entities;
using WK7Bot.Core.Interfaces;
using WK7Bot.Infrastructure.Data;
using Xunit;

namespace WK7Bot.Tests;

public class WishlistDealRepositoryTests : IDisposable
{
    private readonly IDbContextFactory<BotDbContext> _factory;
    private readonly IWishlistDealRepository _repository;

    public WishlistDealRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<BotDbContext>()
            .UseInMemoryDatabase(databaseName: $"wishlist-deal-tests-{Guid.NewGuid()}")
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        _factory = new PooledDbContextFactory<BotDbContext>(options);
        _repository = new WishlistDealRepository(_factory);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }

    private static WishlistGame Game(uint appId, string name) => new()
    {
        AppId = appId,
        Name = name,
        LastUpdated = DateTime.UtcNow
    };

    [Fact]
    public async Task ReplaceTrackedGamesAsync_ReplacesEntireCollection()
    {
        await _repository.ReplaceTrackedGamesAsync(new[] { Game(1, "First"), Game(2, "Second") });
        var initial = await _repository.GetAllTrackedGamesAsync();
        Assert.Equal(2, initial.Count);

        await _repository.ReplaceTrackedGamesAsync(new[] { Game(3, "Third") });
        var replaced = await _repository.GetAllTrackedGamesAsync();
        Assert.Single(replaced);
        Assert.Equal(3u, replaced[0].AppId);
    }

    [Fact]
    public async Task ReplaceTrackedGamesAsync_WithEmptyCollection_ClearsAll()
    {
        await _repository.ReplaceTrackedGamesAsync(new[] { Game(1, "Only") });
        await _repository.ReplaceTrackedGamesAsync(Array.Empty<WishlistGame>());

        Assert.Empty(await _repository.GetAllTrackedGamesAsync());
    }

    [Fact]
    public async Task RecordAlertAsync_AndIsOnCooldownAsync_WorkTogether()
    {
        await _repository.RecordAlertAsync(10, true);

        Assert.True(await _repository.IsOnCooldownAsync(10, TimeSpan.FromHours(1)));
        Assert.False(await _repository.IsOnCooldownAsync(10, TimeSpan.Zero));
        Assert.False(await _repository.IsOnCooldownAsync(11, TimeSpan.FromHours(1)));
    }

    [Fact]
    public async Task PruneAlertHistoryAsync_RemovesOnlyStaleEntries()
    {
        await _repository.RecordAlertAsync(20, false);

        // A positive one-hour retention keeps the just-recorded alert.
        await _repository.PruneAlertHistoryAsync(TimeSpan.FromHours(1));
        Assert.True(await _repository.IsOnCooldownAsync(20, TimeSpan.FromHours(1)));

        // A negative retention moves the cutoff into the future, pruning even the fresh alert.
        await _repository.PruneAlertHistoryAsync(TimeSpan.FromMinutes(-1));
        Assert.False(await _repository.IsOnCooldownAsync(20, TimeSpan.FromHours(1)));
    }

    [Fact]
    public async Task GetCooldownAppIdsAsync_ReturnsOnlyIdsOnCooldown()
    {
        await _repository.RecordAlertAsync(10, true);
        await _repository.RecordAlertAsync(11, false);

        var onCooldown = await _repository.GetCooldownAppIdsAsync(new uint[] { 10, 11, 12 }, TimeSpan.FromHours(1));

        Assert.Contains(10u, onCooldown);
        Assert.Contains(11u, onCooldown);
        Assert.DoesNotContain(12u, onCooldown);
    }

    [Fact]
    public async Task GetCooldownAppIdsAsync_WithEmptyInput_ReturnsEmptySet()
    {
        var onCooldown = await _repository.GetCooldownAppIdsAsync(Array.Empty<uint>(), TimeSpan.FromHours(1));
        Assert.Empty(onCooldown);
    }

    [Fact]
    public async Task RecordAlertsAsync_RecordsMultipleAlertsInOneRoundTrip()
    {
        await _repository.RecordAlertsAsync(new[]
        {
            (30u, true),
            (31u, false)
        });

        Assert.True(await _repository.IsOnCooldownAsync(30, TimeSpan.FromHours(1)));
        Assert.True(await _repository.IsOnCooldownAsync(31, TimeSpan.FromHours(1)));
    }

    [Fact]
    public async Task RecordAlertsAsync_WithEmptyInput_IsNoOp()
    {
        await _repository.RecordAlertsAsync(Array.Empty<(uint, bool)>());

        Assert.False(await _repository.IsOnCooldownAsync(40, TimeSpan.FromHours(1)));
    }
}
