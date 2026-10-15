using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Infrastructure;
using WK7Bot.Core.Entities;
using WK7Bot.Core.Interfaces;
using WK7Bot.Infrastructure.Data;
using Xunit;

namespace WK7Bot.Tests;

public class WarningDispatchRepositoryTests : IDisposable
{
    private readonly IDbContextFactory<BotDbContext> _factory;
    private readonly IWarningDispatchRepository _repository;

    public WarningDispatchRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<BotDbContext>()
            .UseInMemoryDatabase(databaseName: $"warning-dispatch-tests-{Guid.NewGuid()}")
            .Options;

        _factory = new PooledDbContextFactory<BotDbContext>(options);
        _repository = new WarningDispatchRepository(_factory);
    }

    public void Dispose()
    {
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

        using var verifyWarning = _factory.CreateDbContext();
        Assert.Equal(1, verifyWarning.WarningDispatchLogs.Count());
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

        var verifyWarning = _factory.CreateDbContext();
        var stored = Assert.Single(verifyWarning.WarningDispatchLogs.AsNoTracking());
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
        using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<BotDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var context = new BotDbContext(options);

        // Mirrors the startup sequence in Program.cs for a brand-new database.
        await context.Database.EnsureCreatedAsync();
        await context.Database.ExecuteSqlRawAsync(DatabaseInitializationExtensions.CreateWarningDispatchLogsSql);

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

        // An existing database predating the WarningDispatchLogs table only receives the raw SQL
        // (EnsureCreated is a no-op for existing files) — it must create the table and be re-runnable.
        await context.Database.ExecuteSqlRawAsync(DatabaseInitializationExtensions.CreateWarningDispatchLogsSql);
        await context.Database.ExecuteSqlRawAsync(DatabaseInitializationExtensions.CreateWarningDispatchLogsSql);

        var factory = new PooledDbContextFactory<BotDbContext>(options);
        await AssertRoundTripAsync(factory);
    }

    private static async Task AssertRoundTripAsync(IDbContextFactory<BotDbContext> factory)
    {
        var repository = new WarningDispatchRepository(factory);
        var sentAt = new DateTime(2026, 10, 6, 13, 0, 0);

        Assert.False(await repository.HasSentAsync("warn-1", 100));
        await repository.MarkSentAsync("warn-1", 100, sentAt);
        await repository.MarkSentAsync("warn-1", 100, sentAt);
        Assert.True(await repository.HasSentAsync("warn-1", 100));
        Assert.False(await repository.HasSentAsync("warn-1", 200));
        Assert.False(await repository.HasSentAsync("warn-2", 100));
        using var verifyWarning = factory.CreateDbContext();
        Assert.Single(verifyWarning.WarningDispatchLogs.AsNoTracking());
    }
}
