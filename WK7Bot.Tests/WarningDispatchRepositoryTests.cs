using Microsoft.EntityFrameworkCore;
using WK7Bot.Core.Entities;
using WK7Bot.Core.Interfaces;
using WK7Bot.Infrastructure.Data;
using Xunit;

namespace WK7Bot.Tests;

public class WarningDispatchRepositoryTests : IDisposable
{
    private readonly BotDbContext _context;
    private readonly IWarningDispatchRepository _repository;

    public WarningDispatchRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<BotDbContext>()
            .UseInMemoryDatabase(databaseName: $"warning-dispatch-tests-{Guid.NewGuid()}")
            .Options;

        _context = new BotDbContext(options);
        _repository = new WarningDispatchRepository(_context);
    }

    public void Dispose()
    {
        _context.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task HasSentAsync_False_WhenNeverMarked()
    {
        Assert.False(await _repository.HasSentAsync("warn-1", 100));
    }

    [Fact]
    public async Task MarkSentAsync_ThenHasSentAsync_True()
    {
        await _repository.MarkSentAsync("warn-1", 100, new DateTime(2026, 10, 6, 13, 0, 0));

        Assert.True(await _repository.HasSentAsync("warn-1", 100));
    }

    [Fact]
    public async Task MarkSentAsync_IsIdempotent()
    {
        var sentAt = new DateTime(2026, 10, 6, 13, 0, 0);

        await _repository.MarkSentAsync("warn-1", 100, sentAt);
        await _repository.MarkSentAsync("warn-1", 100, sentAt);

        Assert.Equal(1, _context.WarningDispatchLogs.Count());
    }

    [Fact]
    public async Task HasSentAsync_DistinguishesGuilds()
    {
        await _repository.MarkSentAsync("warn-1", 100, new DateTime(2026, 10, 6, 13, 0, 0));

        Assert.True(await _repository.HasSentAsync("warn-1", 100));
        Assert.False(await _repository.HasSentAsync("warn-1", 200));
    }

    [Fact]
    public async Task HasSentAsync_DistinguishesWarnings()
    {
        await _repository.MarkSentAsync("warn-1", 100, new DateTime(2026, 10, 6, 13, 0, 0));

        Assert.True(await _repository.HasSentAsync("warn-1", 100));
        Assert.False(await _repository.HasSentAsync("warn-2", 100));
    }

    [Fact]
    public async Task MarkSentAsync_RecordsSentAt()
    {
        var sentAt = new DateTime(2026, 10, 6, 13, 2, 0);

        await _repository.MarkSentAsync("warn-1", 100, sentAt);

        var stored = Assert.Single(_context.WarningDispatchLogs.AsNoTracking());
        Assert.Equal(sentAt, stored.SentAt);
    }

    [Fact]
    public async Task PruneAsync_RemovesStaleRecords_AndKeepsRecentOnes()
    {
        var now = new DateTime(2026, 10, 6, 13, 0, 0);
        await _repository.MarkSentAsync("warn-old", 100, now.AddDays(-10));
        await _repository.MarkSentAsync("warn-recent", 100, now.AddDays(-1));

        await _repository.PruneAsync(now.AddDays(-7));

        Assert.False(await _repository.HasSentAsync("warn-old", 100));
        Assert.True(await _repository.HasSentAsync("warn-recent", 100));
    }

    [Fact]
    public async Task PruneAsync_WithoutStaleRecords_IsANoOp()
    {
        var now = new DateTime(2026, 10, 6, 13, 0, 0);
        await _repository.MarkSentAsync("warn-1", 100, now);

        await _repository.PruneAsync(now.AddDays(-7));

        Assert.True(await _repository.HasSentAsync("warn-1", 100));
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
        await context.Database.ExecuteSqlRawAsync(DatabaseInitializationExtensions.CreateWarningDispatchLogsSql);

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

        // An existing database predating the WarningDispatchLogs table only receives the raw SQL
        // (EnsureCreated is a no-op for existing files) — it must create the table and be re-runnable.
        await context.Database.ExecuteSqlRawAsync(DatabaseInitializationExtensions.CreateWarningDispatchLogsSql);
        await context.Database.ExecuteSqlRawAsync(DatabaseInitializationExtensions.CreateWarningDispatchLogsSql);

        await AssertRoundTripAsync(context);
    }

    private static async Task AssertRoundTripAsync(BotDbContext context)
    {
        var repository = new WarningDispatchRepository(context);
        var sentAt = new DateTime(2026, 10, 6, 13, 0, 0);

        Assert.False(await repository.HasSentAsync("warn-1", 100));
        await repository.MarkSentAsync("warn-1", 100, sentAt);
        await repository.MarkSentAsync("warn-1", 100, sentAt);
        Assert.True(await repository.HasSentAsync("warn-1", 100));
        Assert.False(await repository.HasSentAsync("warn-1", 200));
        Assert.False(await repository.HasSentAsync("warn-2", 100));
        Assert.Single(context.WarningDispatchLogs.AsNoTracking());
    }
}
