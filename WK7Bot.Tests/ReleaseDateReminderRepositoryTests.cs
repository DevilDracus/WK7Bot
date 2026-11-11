using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using WK7Bot.Core.Entities;
using WK7Bot.Core.Interfaces;
using WK7Bot.Infrastructure.Data;
using Xunit;

namespace WK7Bot.Tests;

public class ReleaseDateReminderRepositoryTests : IDisposable
{
    private readonly IDbContextFactory<BotDbContext> _factory;
    private readonly IReleaseDateReminderRepository _repository;

    public ReleaseDateReminderRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<BotDbContext>()
            .UseInMemoryDatabase(databaseName: $"release-date-reminder-tests-{Guid.NewGuid()}")
            .Options;

        _factory = new PooledDbContextFactory<BotDbContext>(options);
        _repository = new ReleaseDateReminderRepository(_factory);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task AddAsync_ThenGetForUserAsync_ReturnsItem()
    {
        await _repository.AddAsync(new ReleaseDateReminderItem
        {
            DiscordUserId = 100,
            AppId = 123,
            Name = "Test Game",
            ReleaseDate = new DateTime(2027, 1, 15)
        });

        var items = await _repository.GetForUserAsync(100);

        var item = Assert.Single(items);
        Assert.Equal(123u, item.AppId);
        Assert.Equal("Test Game", item.Name);
        Assert.Equal(new DateTime(2027, 1, 15), item.ReleaseDate);
    }

    [Fact]
    public async Task GetForUserAsync_DistinguishesUsers()
    {
        await _repository.AddAsync(new ReleaseDateReminderItem { DiscordUserId = 100, AppId = 1, Name = "A" });
        await _repository.AddAsync(new ReleaseDateReminderItem { DiscordUserId = 200, AppId = 2, Name = "B" });

        var items = await _repository.GetForUserAsync(100);

        var item = Assert.Single(items);
        Assert.Equal(1u, item.AppId);
    }

    [Fact]
    public async Task RemoveAsync_DeletesMatchingItem()
    {
        await _repository.AddAsync(new ReleaseDateReminderItem { DiscordUserId = 100, AppId = 1, Name = "A" });

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
    public async Task UpdateAsync_PersistsChanges()
    {
        var item = new ReleaseDateReminderItem
        {
            DiscordUserId = 100,
            AppId = 1,
            Name = "A"
        };
        await _repository.AddAsync(item);

        item.Name = "Renamed";
        item.ReleaseDate = new DateTime(2027, 2, 1);
        item.LastNotifiedAt = new DateTime(2026, 11, 11);
        await _repository.UpdateAsync(item);

        var stored = await _repository.GetForUserAsync(100);
        var updated = Assert.Single(stored);
        Assert.Equal("Renamed", updated.Name);
        Assert.Equal(new DateTime(2027, 2, 1), updated.ReleaseDate);
        Assert.Equal(new DateTime(2026, 11, 11), updated.LastNotifiedAt);
    }

    [Fact]
    public async Task UpdateManyAsync_PersistsChangesForMultipleItems()
    {
        var first = new ReleaseDateReminderItem { DiscordUserId = 100, AppId = 1, Name = "A", ReleaseDate = new DateTime(2027, 1, 1) };
        var second = new ReleaseDateReminderItem { DiscordUserId = 100, AppId = 2, Name = "B", ReleaseDate = new DateTime(2027, 2, 1) };
        var third = new ReleaseDateReminderItem { DiscordUserId = 100, AppId = 3, Name = "C", ReleaseDate = new DateTime(2027, 3, 1) };
        await _repository.AddAsync(first);
        await _repository.AddAsync(second);
        await _repository.AddAsync(third);

        var updatedFirst = new ReleaseDateReminderItem
        {
            Id = first.Id,
            DiscordUserId = first.DiscordUserId,
            AppId = first.AppId,
            Name = "A Updated",
            ReleaseDate = new DateTime(2027, 1, 15),
            IsTba = true,
            LastNotifiedAt = new DateTime(2026, 11, 11)
        };
        var updatedSecond = new ReleaseDateReminderItem
        {
            Id = second.Id,
            DiscordUserId = second.DiscordUserId,
            AppId = second.AppId,
            Name = "B Updated",
            ReleaseDate = new DateTime(2027, 2, 15),
            IsTba = false,
            LastNotifiedAt = new DateTime(2026, 11, 12)
        };

        await _repository.UpdateManyAsync(new[] { updatedFirst, updatedSecond });

        var stored = await _repository.GetForUserAsync(100);
        var byAppId = stored.ToDictionary(i => i.AppId);
        Assert.Equal("A Updated", byAppId[1].Name);
        Assert.Equal(new DateTime(2027, 1, 15), byAppId[1].ReleaseDate);
        Assert.True(byAppId[1].IsTba);
        Assert.Equal(new DateTime(2026, 11, 11), byAppId[1].LastNotifiedAt);
        Assert.Equal("B Updated", byAppId[2].Name);
        Assert.Equal(new DateTime(2027, 2, 15), byAppId[2].ReleaseDate);
        Assert.Equal("C", byAppId[3].Name);
    }

    [Fact]
    public async Task UpdateManyAsync_WithEmptyInput_IsNoOp()
    {
        var item = new ReleaseDateReminderItem { DiscordUserId = 100, AppId = 1, Name = "A" };
        await _repository.AddAsync(item);

        await _repository.UpdateManyAsync(Array.Empty<ReleaseDateReminderItem>());

        var stored = await _repository.GetForUserAsync(100);
        Assert.Equal("A", Assert.Single(stored).Name);
    }

    [Fact]
    public async Task SqliteStartupSequence_EnsureCreatedPlusRawSql_RoundTrips()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<BotDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var context = new BotDbContext(options);
        await context.Database.EnsureCreatedAsync();
        await context.Database.ExecuteSqlRawAsync(DatabaseSchemaInitializer.CreateReleaseDateReminderItemsSql);

        var factory = new PooledDbContextFactory<BotDbContext>(options);
        await AssertRoundTripAsync(factory);
    }

    [Fact]
    public async Task SqliteMigrationSequence_RawSql_CreatesMissingTableIdempotently_And_RoundTrips()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<BotDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var context = new BotDbContext(options);
        await context.Database.ExecuteSqlRawAsync(DatabaseSchemaInitializer.CreateReleaseDateReminderItemsSql);
        await context.Database.ExecuteSqlRawAsync(DatabaseSchemaInitializer.CreateReleaseDateReminderItemsSql);

        var factory = new PooledDbContextFactory<BotDbContext>(options);
        await AssertRoundTripAsync(factory);
    }

    private static async Task AssertRoundTripAsync(IDbContextFactory<BotDbContext> factory)
    {
        var repository = new ReleaseDateReminderRepository(factory);

        await repository.AddAsync(new ReleaseDateReminderItem
        {
            DiscordUserId = 100,
            AppId = 123,
            Name = "Game"
        });

        var items = await repository.GetForUserAsync(100);
        var item = Assert.Single(items);
        Assert.Equal(123u, item.AppId);

        Assert.True(await repository.RemoveAsync(100, 123));
        Assert.Empty(await repository.GetForUserAsync(100));
    }
}
