using Microsoft.EntityFrameworkCore;
using WK7Bot.Core.Entities;
using WK7Bot.Core.Interfaces;
using WK7Bot.Infrastructure.Data;
using Xunit;

namespace WK7Bot.Tests;

public class WasteDispatchRepositoryTests : IDisposable
{
    private readonly BotDbContext _context;
    private readonly IWasteDispatchRepository _repository;

    public WasteDispatchRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<BotDbContext>()
            .UseInMemoryDatabase(databaseName: $"waste-dispatch-tests-{Guid.NewGuid()}")
            .Options;

        _context = new BotDbContext(options);
        _repository = new WasteDispatchRepository(_context);
    }

    public void Dispose()
    {
        _context.Dispose();
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

        Assert.Equal(1, _context.WasteDispatchLogs.Count());
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
        var stored = Assert.Single(_context.WasteDispatchLogs.AsNoTracking());
        Assert.Equal(date, stored.SentOn);
    }

    [Fact]
    public async Task SqliteStartupSequence_EnsureCreatedPlusRawSql_RoundTrips()
    {
        var options = new DbContextOptionsBuilder<BotDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;

        await using var context = new BotDbContext(options);
        await context.Database.OpenConnectionAsync();

        // Mirrors the startup sequence in Program.cs for a brand-new database.
        await context.Database.EnsureCreatedAsync();
        await context.Database.ExecuteSqlRawAsync(DatabaseInitializationExtensions.CreateWasteDispatchLogsSql);

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

        // An existing database predating the WasteDispatchLogs table only receives the raw SQL
        // (EnsureCreated is a no-op for existing files) — it must create the table and be re-runnable.
        await context.Database.ExecuteSqlRawAsync(DatabaseInitializationExtensions.CreateWasteDispatchLogsSql);
        await context.Database.ExecuteSqlRawAsync(DatabaseInitializationExtensions.CreateWasteDispatchLogsSql);

        await AssertRoundTripAsync(context);
    }

    [Fact]
    public async Task MarkSentAsync_RowAlreadyWrittenByAnotherContext_IsIdempotent()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"wk7-waste-dispatch-{Guid.NewGuid():N}.db");

        try
        {
            var connectionString = $"Data Source={databasePath};Pooling=False";

            await using (var firstContext = new BotDbContext(new DbContextOptionsBuilder<BotDbContext>().UseSqlite(connectionString).Options))
            {
                await firstContext.Database.EnsureCreatedAsync();
                await new WasteDispatchRepository(firstContext).MarkSentAsync(WasteDispatchKinds.DailyConfirmation, 100, new DateTime(2026, 10, 5));
            }

            // A second context has no tracked entity, but the repository's existence pre-check sees
            // the committed row and short-circuits — the write is recorded exactly once across
            // contexts. (The duplicate-insert guard itself is covered by DatabaseWriteGuardTests.)
            await using var secondContext = new BotDbContext(new DbContextOptionsBuilder<BotDbContext>().UseSqlite(connectionString).Options);
            await new WasteDispatchRepository(secondContext).MarkSentAsync(WasteDispatchKinds.DailyConfirmation, 100, new DateTime(2026, 10, 5));

            Assert.Equal(1, await secondContext.WasteDispatchLogs.CountAsync());
            Assert.True(await new WasteDispatchRepository(secondContext).HasSentAsync(WasteDispatchKinds.DailyConfirmation, 100, new DateTime(2026, 10, 5)));
        }
        finally
        {
            File.Delete(databasePath);
        }
    }

    private static async Task AssertRoundTripAsync(BotDbContext context)
    {
        var repository = new WasteDispatchRepository(context);
        var date = new DateTime(2026, 10, 5);

        Assert.False(await repository.HasSentAsync(WasteDispatchKinds.DailyConfirmation, 100, date));
        await repository.MarkSentAsync(WasteDispatchKinds.DailyConfirmation, 100, date);
        await repository.MarkSentAsync(WasteDispatchKinds.DailyConfirmation, 100, date);
        Assert.True(await repository.HasSentAsync(WasteDispatchKinds.DailyConfirmation, 100, date));
        Assert.False(await repository.HasSentAsync(WasteDispatchKinds.WeeklyOverview, 100, date));
        Assert.Single(context.WasteDispatchLogs.AsNoTracking());
    }
}
