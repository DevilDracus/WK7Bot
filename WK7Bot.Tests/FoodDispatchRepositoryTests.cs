using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Infrastructure;
using WK7Bot.Core.Entities;
using WK7Bot.Core.Interfaces;
using WK7Bot.Infrastructure.Data;
using Xunit;

namespace WK7Bot.Tests;

public class FoodDispatchRepositoryTests : IDisposable
{
    private readonly IDbContextFactory<BotDbContext> _factory;
    private readonly IFoodDispatchRepository _repository;

    public FoodDispatchRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<BotDbContext>()
            .UseInMemoryDatabase(databaseName: $"food-dispatch-tests-{Guid.NewGuid()}")
            .Options;

        _factory = new PooledDbContextFactory<BotDbContext>(options);
        _repository = new FoodDispatchRepository(_factory);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task HasSentAsync_False_WhenNeverMarked()
    {
        Assert.False(await _repository.HasSentAsync(FoodDispatchKinds.MonthlyProduce, new DateTime(2026, 10, 1)));
    }

    [Fact]
    public async Task MarkSentAsync_ThenHasSentAsync_True()
    {
        await _repository.MarkSentAsync(FoodDispatchKinds.MonthlyProduce, new DateTime(2026, 10, 1));

        Assert.True(await _repository.HasSentAsync(FoodDispatchKinds.MonthlyProduce, new DateTime(2026, 10, 1)));
    }

    [Fact]
    public async Task MarkSentAsync_IsIdempotent()
    {
        var date = new DateTime(2026, 10, 1);

        await _repository.MarkSentAsync(FoodDispatchKinds.MonthlyProduce, date);
        await _repository.MarkSentAsync(FoodDispatchKinds.MonthlyProduce, date);

        using var verifyFood = _factory.CreateDbContext();
        Assert.Equal(1, verifyFood.FoodDispatchLogs.Count());
    }

    [Fact]
    public async Task HasSentAsync_DistinguishesKinds()
    {
        var date = new DateTime(2026, 10, 1);
        await _repository.MarkSentAsync(FoodDispatchKinds.MonthlyProduce, date);

        Assert.True(await _repository.HasSentAsync(FoodDispatchKinds.MonthlyProduce, date));
        Assert.False(await _repository.HasSentAsync(FoodDispatchKinds.WeeklyRecipe, date));
    }

    [Fact]
    public async Task HasSentAsync_DistinguishesDates()
    {
        var date = new DateTime(2026, 10, 1);
        await _repository.MarkSentAsync(FoodDispatchKinds.WeeklyRecipe, date);

        Assert.True(await _repository.HasSentAsync(FoodDispatchKinds.WeeklyRecipe, date));
        Assert.False(await _repository.HasSentAsync(FoodDispatchKinds.WeeklyRecipe, date.AddDays(7)));
    }

    [Fact]
    public async Task MarkSentAsync_NormalizesTimeOfDay()
    {
        var date = new DateTime(2026, 10, 1);
        var withTime = date.AddHours(15).AddMinutes(30);

        await _repository.MarkSentAsync(FoodDispatchKinds.WeeklyRecipe, withTime);

        Assert.True(await _repository.HasSentAsync(FoodDispatchKinds.WeeklyRecipe, date));
        var verifyFood = _factory.CreateDbContext();
        var stored = Assert.Single(verifyFood.FoodDispatchLogs.AsNoTracking());
        Assert.Equal(date, stored.SentOn);
    }

    [Fact]
    public async Task PruneAsync_RemovesOnlyRecordsOlderThanCutoff()
    {
        await _repository.MarkSentAsync(FoodDispatchKinds.MonthlyProduce, new DateTime(2026, 8, 1));
        await _repository.MarkSentAsync(FoodDispatchKinds.WeeklyRecipe, new DateTime(2026, 10, 1));

        var removed = await _repository.PruneAsync(new DateTime(2026, 9, 15));

        Assert.Equal(1, removed);
        using var verifyFood = _factory.CreateDbContext();
        Assert.Equal(1, verifyFood.FoodDispatchLogs.Count());
        Assert.True(await _repository.HasSentAsync(FoodDispatchKinds.WeeklyRecipe, new DateTime(2026, 10, 1)));
    }

    [Fact]
    public async Task PruneAsync_Zero_WhenNothingOlder()
    {
        await _repository.MarkSentAsync(FoodDispatchKinds.MonthlyProduce, new DateTime(2026, 10, 1));

        var removed = await _repository.PruneAsync(new DateTime(2026, 9, 15));

        Assert.Equal(0, removed);
        using var verifyFood = _factory.CreateDbContext();
        Assert.Equal(1, verifyFood.FoodDispatchLogs.Count());
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

        // Mirrors the startup sequence in DatabaseInitializationExtensions for a brand-new database.
        await context.Database.EnsureCreatedAsync();
        await context.Database.ExecuteSqlRawAsync(DatabaseInitializationExtensions.CreateFoodDispatchLogsSql);

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

        // An existing database predating the FoodDispatchLogs table only receives the raw SQL
        // (EnsureCreated is a no-op for existing files) — it must create the table and be re-runnable.
        await context.Database.ExecuteSqlRawAsync(DatabaseInitializationExtensions.CreateFoodDispatchLogsSql);
        await context.Database.ExecuteSqlRawAsync(DatabaseInitializationExtensions.CreateFoodDispatchLogsSql);

        var factory = new PooledDbContextFactory<BotDbContext>(options);
        await AssertRoundTripAsync(factory);
    }

    private static async Task AssertRoundTripAsync(IDbContextFactory<BotDbContext> factory)
    {
        var repository = new FoodDispatchRepository(factory);
        var date = new DateTime(2026, 10, 1);

        Assert.False(await repository.HasSentAsync(FoodDispatchKinds.MonthlyProduce, date));
        await repository.MarkSentAsync(FoodDispatchKinds.MonthlyProduce, date);
        await repository.MarkSentAsync(FoodDispatchKinds.MonthlyProduce, date);
        Assert.True(await repository.HasSentAsync(FoodDispatchKinds.MonthlyProduce, date));
        Assert.False(await repository.HasSentAsync(FoodDispatchKinds.WeeklyRecipe, date));
        using var verifyFood = factory.CreateDbContext();
        Assert.Single(verifyFood.FoodDispatchLogs.AsNoTracking());
    }
}
