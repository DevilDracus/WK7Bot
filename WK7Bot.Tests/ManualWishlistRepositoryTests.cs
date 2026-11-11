using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using WK7Bot.Core.Entities;
using WK7Bot.Core.Interfaces;
using WK7Bot.Infrastructure.Data;
using Xunit;

namespace WK7Bot.Tests;

public class ManualWishlistRepositoryTests : IDisposable
{
    private readonly IDbContextFactory<BotDbContext> _factory;
    private readonly IManualWishlistRepository _repository;

    public ManualWishlistRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<BotDbContext>()
            .UseInMemoryDatabase(databaseName: $"manual-wishlist-tests-{Guid.NewGuid()}")
            .Options;

        _factory = new PooledDbContextFactory<BotDbContext>(options);
        _repository = new ManualWishlistRepository(_factory);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task AddAsync_ThenGetForUserAsync_ReturnsItem()
    {
        await _repository.AddAsync(new ManualWishlistItem
        {
            DiscordUserId = 100,
            AppId = 123456,
            Name = "Test Game"
        });

        var items = await _repository.GetForUserAsync(100);

        var item = Assert.Single(items);
        Assert.Equal(123456u, item.AppId);
        Assert.Equal("Test Game", item.Name);
    }

    [Fact]
    public async Task GetForUserAsync_DistinguishesUsers()
    {
        await _repository.AddAsync(new ManualWishlistItem { DiscordUserId = 100, AppId = 1, Name = "A" });
        await _repository.AddAsync(new ManualWishlistItem { DiscordUserId = 200, AppId = 2, Name = "B" });

        var items = await _repository.GetForUserAsync(100);

        var item = Assert.Single(items);
        Assert.Equal(1u, item.AppId);
    }

    [Fact]
    public async Task RemoveAsync_DeletesMatchingItem()
    {
        await _repository.AddAsync(new ManualWishlistItem { DiscordUserId = 100, AppId = 1, Name = "A" });

        var removed = await _repository.RemoveAsync(100, 1);

        Assert.True(removed);
        Assert.Empty(await _repository.GetForUserAsync(100));
    }

    [Fact]
    public async Task RemoveAsync_ReturnsFalse_WhenNotFound()
    {
        var removed = await _repository.RemoveAsync(100, 999);

        Assert.False(removed);
    }

    [Fact]
    public async Task RemoveAsync_DistinguishesUsers()
    {
        await _repository.AddAsync(new ManualWishlistItem { DiscordUserId = 100, AppId = 1, Name = "A" });

        var removed = await _repository.RemoveAsync(200, 1);

        Assert.False(removed);
        Assert.Single(await _repository.GetForUserAsync(100));
    }

    [Fact]
    public async Task SqliteStartupSequence_EnsureCreatedPlusRawSql_RoundTrips()
    {
        using var connection = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<BotDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var context = new BotDbContext(options);
        await context.Database.EnsureCreatedAsync();
        await context.Database.ExecuteSqlRawAsync(DatabaseSchemaInitializer.CreateManualWishlistItemsSql);

        var factory = new PooledDbContextFactory<BotDbContext>(options);
        await AssertRoundTripAsync(factory);
    }

    [Fact]
    public async Task SqliteMigrationSequence_RawSql_CreatesMissingTableIdempotently_And_RoundTrips()
    {
        using var connection = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<BotDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var context = new BotDbContext(options);
        await context.Database.ExecuteSqlRawAsync(DatabaseSchemaInitializer.CreateManualWishlistItemsSql);
        await context.Database.ExecuteSqlRawAsync(DatabaseSchemaInitializer.CreateManualWishlistItemsSql);

        var factory = new PooledDbContextFactory<BotDbContext>(options);
        await AssertRoundTripAsync(factory);
    }

    private static async Task AssertRoundTripAsync(IDbContextFactory<BotDbContext> factory)
    {
        var repository = new ManualWishlistRepository(factory);

        await repository.AddAsync(new ManualWishlistItem { DiscordUserId = 100, AppId = 123, Name = "Game" });

        var items = await repository.GetForUserAsync(100);
        var item = Assert.Single(items);
        Assert.Equal(123u, item.AppId);

        Assert.True(await repository.RemoveAsync(100, 123));
        Assert.Empty(await repository.GetForUserAsync(100));
    }
}
