using Microsoft.EntityFrameworkCore;
using WK7Bot.Core.Entities;
using WK7Bot.Core.Interfaces;
using WK7Bot.Infrastructure.Data;
using Xunit;

namespace WK7Bot.Tests;

public class FoodDispatchRepositoryTests : IDisposable
{
    private readonly BotDbContext _context;
    private readonly IFoodDispatchRepository _repository;

    public FoodDispatchRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<BotDbContext>()
            .UseInMemoryDatabase(databaseName: $"food-dispatch-tests-{Guid.NewGuid()}")
            .Options;

        _context = new BotDbContext(options);
        _repository = new FoodDispatchRepository(_context);
    }

    public void Dispose()
    {
        _context.Dispose();
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

        Assert.Equal(1, _context.FoodDispatchLogs.Count());
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
        var stored = Assert.Single(_context.FoodDispatchLogs.AsNoTracking());
        Assert.Equal(date, stored.SentOn);
    }

    [Fact]
    public async Task PruneAsync_RemovesOnlyRecordsOlderThanCutoff()
    {
        await _repository.MarkSentAsync(FoodDispatchKinds.MonthlyProduce, new DateTime(2026, 8, 1));
        await _repository.MarkSentAsync(FoodDispatchKinds.WeeklyRecipe, new DateTime(2026, 10, 1));

        var removed = await _repository.PruneAsync(new DateTime(2026, 9, 15));

        Assert.Equal(1, removed);
        Assert.Equal(1, _context.FoodDispatchLogs.Count());
        Assert.True(await _repository.HasSentAsync(FoodDispatchKinds.WeeklyRecipe, new DateTime(2026, 10, 1)));
    }

    [Fact]
    public async Task PruneAsync_Zero_WhenNothingOlder()
    {
        await _repository.MarkSentAsync(FoodDispatchKinds.MonthlyProduce, new DateTime(2026, 10, 1));

        var removed = await _repository.PruneAsync(new DateTime(2026, 9, 15));

        Assert.Equal(0, removed);
        Assert.Equal(1, _context.FoodDispatchLogs.Count());
    }

    [Fact]
    public async Task SqliteStartupSequence_EnsureCreatedPlusRawSql_RoundTrips()
    {
        var options = new DbContextOptionsBuilder<BotDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;

        await using var context = new BotDbContext(options);
        await context.Database.OpenConnectionAsync();

        // Mirrors the startup sequence in DatabaseInitializationExtensions for a brand-new database.
        await context.Database.EnsureCreatedAsync();
        await context.Database.ExecuteSqlRawAsync(DatabaseInitializationExtensions.CreateFoodDispatchLogsSql);

        await AssertRoundTripAsync(context);
    }

    [Fact]
    public async Task SqliteMigrationSequence_RawSql_CreatesMissingTableIdempotently_And_RoundTrips()
    {
        var options = new DbContextOptionsBuilder<BotDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;

        await using var context = new BotDbContext(options);
        await context.Database.OpenConnectionAsync();

        // An existing database predating the FoodDispatchLogs table only receives the raw SQL
        // (EnsureCreated is a no-op for existing files) — it must create the table and be re-runnable.
        await context.Database.ExecuteSqlRawAsync(DatabaseInitializationExtensions.CreateFoodDispatchLogsSql);
        await context.Database.ExecuteSqlRawAsync(DatabaseInitializationExtensions.CreateFoodDispatchLogsSql);

        await AssertRoundTripAsync(context);
    }

    private static async Task AssertRoundTripAsync(BotDbContext context)
    {
        var repository = new FoodDispatchRepository(context);
        var date = new DateTime(2026, 10, 1);

        Assert.False(await repository.HasSentAsync(FoodDispatchKinds.MonthlyProduce, date));
        await repository.MarkSentAsync(FoodDispatchKinds.MonthlyProduce, date);
        await repository.MarkSentAsync(FoodDispatchKinds.MonthlyProduce, date);
        Assert.True(await repository.HasSentAsync(FoodDispatchKinds.MonthlyProduce, date));
        Assert.False(await repository.HasSentAsync(FoodDispatchKinds.WeeklyRecipe, date));
        Assert.Single(context.FoodDispatchLogs.AsNoTracking());
    }
}
