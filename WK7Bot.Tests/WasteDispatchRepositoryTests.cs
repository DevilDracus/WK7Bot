using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using WK7Bot.Core.Entities;
using WK7Bot.Core.Interfaces;
using WK7Bot.Infrastructure.Data;
using Xunit;

namespace WK7Bot.Tests;

public class WasteDispatchRepositoryTests : IDisposable
{
    private readonly IDbContextFactory<BotDbContext> _factory;
    private readonly IWasteDispatchRepository _repository;

    public WasteDispatchRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<BotDbContext>()
            .UseInMemoryDatabase(databaseName: $"waste-dispatch-tests-{Guid.NewGuid()}")
            .Options;

        _factory = new PooledDbContextFactory<BotDbContext>(options);
        _repository = new WasteDispatchRepository(_factory);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task HasSentAsync_False_WhenNeverMarked()
    {
        Assert.False(await _repository.HasSentAsync(WasteDispatchKinds.DailyConfirmation, 100, new DateTime(2026, 10, 5)));
    }

    [Fact]
    public async Task MarkSentAsync_ThenHasSentAsync_True()
    {
        await _repository.MarkSentAsync(WasteDispatchKinds.DailyConfirmation, 100, new DateTime(2026, 10, 5));

        Assert.True(await _repository.HasSentAsync(WasteDispatchKinds.DailyConfirmation, 100, new DateTime(2026, 10, 5)));
    }

    [Fact]
    public async Task MarkSentAsync_IsIdempotent()
    {
        var date = new DateTime(2026, 10, 5);

        await _repository.MarkSentAsync(WasteDispatchKinds.DailyConfirmation, 100, date);
        await _repository.MarkSentAsync(WasteDispatchKinds.DailyConfirmation, 100, date);

        using var verifyWaste = _factory.CreateDbContext();
        Assert.Equal(1, verifyWaste.WasteDispatchLogs.Count());
    }

    [Fact]
    public async Task HasSentAsync_DistinguishesKinds()
    {
        await _repository.MarkSentAsync(WasteDispatchKinds.DailyConfirmation, 100, new DateTime(2026, 10, 5));

        Assert.True(await _repository.HasSentAsync(WasteDispatchKinds.DailyConfirmation, 100, new DateTime(2026, 10, 5)));
        Assert.False(await _repository.HasSentAsync(WasteDispatchKinds.WeeklyOverview, 100, new DateTime(2026, 10, 5)));
    }

    [Fact]
    public async Task HasSentAsync_DistinguishesGuilds()
    {
        await _repository.MarkSentAsync(WasteDispatchKinds.DailyConfirmation, 100, new DateTime(2026, 10, 5));

        Assert.True(await _repository.HasSentAsync(WasteDispatchKinds.DailyConfirmation, 100, new DateTime(2026, 10, 5)));
        Assert.False(await _repository.HasSentAsync(WasteDispatchKinds.DailyConfirmation, 200, new DateTime(2026, 10, 5)));
    }

    [Fact]
    public async Task HasSentAsync_DistinguishesDates()
    {
        await _repository.MarkSentAsync(WasteDispatchKinds.DailyConfirmation, 100, new DateTime(2026, 10, 5));

        Assert.True(await _repository.HasSentAsync(WasteDispatchKinds.DailyConfirmation, 100, new DateTime(2026, 10, 5)));
        Assert.False(await _repository.HasSentAsync(WasteDispatchKinds.DailyConfirmation, 100, new DateTime(2026, 10, 6)));
    }

    [Fact]
    public async Task MarkSentAsync_NormalizesTimeOfDay()
    {
        var date = new DateTime(2026, 10, 5);
        var withTime = date.AddHours(14).AddMinutes(35);

        await _repository.MarkSentAsync(WasteDispatchKinds.DailyConfirmation, 100, withTime);

        Assert.True(await _repository.HasSentAsync(WasteDispatchKinds.DailyConfirmation, 100, date));
        var verifyWaste = _factory.CreateDbContext();
        var stored = Assert.Single(verifyWaste.WasteDispatchLogs.AsNoTracking());
        Assert.Equal(date, stored.SentOn);
    }

    [Fact]
    public async Task FilterNotSentAsync_ReturnsAll_WhenNoneMarked()
    {
        var guildIds = new[] { 100ul, 200ul, 300ul };
        var date = new DateTime(2026, 10, 5);

        var notSent = await _repository.FilterNotSentAsync(guildIds, WasteDispatchKinds.DailyConfirmation, date);

        Assert.Equal(guildIds.OrderBy(id => id), notSent.OrderBy(id => id));
    }

    [Fact]
    public async Task FilterNotSentAsync_ReturnsOnlyUnmarked()
    {
        var guildIds = new[] { 100ul, 200ul, 300ul };
        var date = new DateTime(2026, 10, 5);

        await _repository.MarkSentAsync(WasteDispatchKinds.DailyConfirmation, 200, date);

        var notSent = await _repository.FilterNotSentAsync(guildIds, WasteDispatchKinds.DailyConfirmation, date);

        Assert.Equal(new[] { 100ul, 300ul }, notSent.OrderBy(id => id));
    }

    [Fact]
    public async Task FilterNotSentAsync_ReturnsEmpty_WhenAllMarked()
    {
        var guildIds = new[] { 100ul, 200ul };
        var date = new DateTime(2026, 10, 5);

        await _repository.MarkSentManyAsync(guildIds, WasteDispatchKinds.DailyConfirmation, date);

        var notSent = await _repository.FilterNotSentAsync(guildIds, WasteDispatchKinds.DailyConfirmation, date);

        Assert.Empty(notSent);
    }

    [Fact]
    public async Task FilterNotSentAsync_DistinguishesKinds()
    {
        var guildIds = new[] { 100ul };
        var date = new DateTime(2026, 10, 5);

        await _repository.MarkSentAsync(WasteDispatchKinds.DailyConfirmation, 100, date);

        var notSent = await _repository.FilterNotSentAsync(guildIds, WasteDispatchKinds.WeeklyOverview, date);

        Assert.Equal(guildIds, notSent);
    }

    [Fact]
    public async Task FilterNotSentAsync_DistinguishesDates()
    {
        var guildIds = new[] { 100ul };
        var date = new DateTime(2026, 10, 5);

        await _repository.MarkSentAsync(WasteDispatchKinds.DailyConfirmation, 100, date);

        var notSent = await _repository.FilterNotSentAsync(guildIds, WasteDispatchKinds.DailyConfirmation, date.AddDays(1));

        Assert.Equal(guildIds, notSent);
    }

    [Fact]
    public async Task MarkSentManyAsync_InsertsMultipleRecords()
    {
        var guildIds = new[] { 100ul, 200ul, 300ul };
        var date = new DateTime(2026, 10, 5);

        await _repository.MarkSentManyAsync(guildIds, WasteDispatchKinds.DailyConfirmation, date);

        foreach (var guildId in guildIds)
        {
            Assert.True(await _repository.HasSentAsync(WasteDispatchKinds.DailyConfirmation, guildId, date));
        }

        using var verifyWaste = _factory.CreateDbContext();
        Assert.Equal(3, verifyWaste.WasteDispatchLogs.Count());
    }

    [Fact]
    public async Task MarkSentManyAsync_IsIdempotent()
    {
        var guildIds = new[] { 100ul, 200ul };
        var date = new DateTime(2026, 10, 5);

        await _repository.MarkSentManyAsync(guildIds, WasteDispatchKinds.DailyConfirmation, date);
        await _repository.MarkSentManyAsync(guildIds, WasteDispatchKinds.DailyConfirmation, date);

        using var verifyWaste = _factory.CreateDbContext();
        Assert.Equal(2, verifyWaste.WasteDispatchLogs.Count());
    }

    [Fact]
    public async Task MarkSentManyAsync_SkipsAlreadySent()
    {
        var guildIds = new[] { 100ul, 200ul, 300ul };
        var date = new DateTime(2026, 10, 5);

        await _repository.MarkSentAsync(WasteDispatchKinds.DailyConfirmation, 200, date);
        await _repository.MarkSentManyAsync(guildIds, WasteDispatchKinds.DailyConfirmation, date);

        using var verifyWaste = _factory.CreateDbContext();
        Assert.Equal(3, verifyWaste.WasteDispatchLogs.Count());
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

        // Mirrors the startup sequence in Program.cs for a brand-new database.
        await context.Database.EnsureCreatedAsync();
        await context.Database.ExecuteSqlRawAsync(DatabaseInitializationExtensions.CreateWasteDispatchLogsSql);

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

        // An existing database predating the WasteDispatchLogs table only receives the raw SQL
        // (EnsureCreated is a no-op for existing files) — it must create the table and be re-runnable.
        await context.Database.ExecuteSqlRawAsync(DatabaseInitializationExtensions.CreateWasteDispatchLogsSql);
        await context.Database.ExecuteSqlRawAsync(DatabaseInitializationExtensions.CreateWasteDispatchLogsSql);

        var factory = new PooledDbContextFactory<BotDbContext>(options);
        await AssertRoundTripAsync(factory);
    }

    [Fact]
    public async Task MarkSentAsync_CalledTwice_IsIdempotent()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"wk7-waste-dispatch-{Guid.NewGuid():N}.db");

        try
        {
            var connectionString = $"Data Source={databasePath}";

            var options = new DbContextOptionsBuilder<BotDbContext>().UseSqlite(connectionString).Options;
            await using var context = new BotDbContext(options);
            await context.Database.EnsureCreatedAsync();

            var factory = new PooledDbContextFactory<BotDbContext>(options);
            var repository = new WasteDispatchRepository(factory);
            var date = new DateTime(2026, 10, 5);

            await repository.MarkSentAsync(WasteDispatchKinds.DailyConfirmation, 100, date);
            await repository.MarkSentAsync(WasteDispatchKinds.DailyConfirmation, 100, date);

            Assert.Equal(1, await context.WasteDispatchLogs.CountAsync());
            Assert.True(await repository.HasSentAsync(WasteDispatchKinds.DailyConfirmation, 100, date));
        }
        finally
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            try
            { File.Delete(databasePath); }
            catch { /* Windows may still hold the SQLite handle; best-effort cleanup. */ }
        }
    }

    private static async Task AssertRoundTripAsync(IDbContextFactory<BotDbContext> factory)
    {
        var repository = new WasteDispatchRepository(factory);
        var date = new DateTime(2026, 10, 5);

        Assert.False(await repository.HasSentAsync(WasteDispatchKinds.DailyConfirmation, 100, date));
        await repository.MarkSentAsync(WasteDispatchKinds.DailyConfirmation, 100, date);
        await repository.MarkSentAsync(WasteDispatchKinds.DailyConfirmation, 100, date);
        Assert.True(await repository.HasSentAsync(WasteDispatchKinds.DailyConfirmation, 100, date));
        Assert.False(await repository.HasSentAsync(WasteDispatchKinds.WeeklyOverview, 100, date));
        using var verifyWaste = factory.CreateDbContext();
        Assert.Single(verifyWaste.WasteDispatchLogs.AsNoTracking());
    }
}
