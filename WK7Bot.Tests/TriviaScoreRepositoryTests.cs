using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using WK7Bot.Core.Entities;
using WK7Bot.Core.Interfaces;
using WK7Bot.Infrastructure.Data;
using Xunit;

namespace WK7Bot.Tests;

public class TriviaScoreRepositoryTests : IDisposable
{
    private readonly IDbContextFactory<BotDbContext> _factory;
    private readonly ITriviaScoreRepository _repository;

    public TriviaScoreRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<BotDbContext>()
            .UseInMemoryDatabase(databaseName: $"trivia-tests-{Guid.NewGuid()}")
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        _factory = new PooledDbContextFactory<BotDbContext>(options);
        _repository = new TriviaScoreRepository(_factory);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task IncrementAsync_CreatesScore_WhenMissing()
    {
        var weekStart = new DateTime(2026, 10, 5, 0, 0, 0, DateTimeKind.Utc);

        await _repository.IncrementAsync(1, 10, weekStart, 3);

        var top = await _repository.GetTopAsync(10, weekStart, 10);
        var score = Assert.Single(top);
        Assert.Equal(1ul, score.DiscordUserId);
        Assert.Equal(3, score.Score);
    }

    [Fact]
    public async Task IncrementAsync_AccumulatesScore_WhenExists()
    {
        var weekStart = new DateTime(2026, 10, 5, 0, 0, 0, DateTimeKind.Utc);

        await _repository.IncrementAsync(2, 10, weekStart, 2);
        await _repository.IncrementAsync(2, 10, weekStart, 5);

        var top = await _repository.GetTopAsync(10, weekStart, 10);
        var score = Assert.Single(top);
        Assert.Equal(2ul, score.DiscordUserId);
        Assert.Equal(7, score.Score);
    }

    [Fact]
    public async Task GetTopAsync_OrdersByScoreDescending()
    {
        var weekStart = new DateTime(2026, 10, 5, 0, 0, 0, DateTimeKind.Utc);

        await _repository.IncrementAsync(3, 10, weekStart, 1);
        await _repository.IncrementAsync(4, 10, weekStart, 9);
        await _repository.IncrementAsync(5, 10, weekStart, 5);

        var top = await _repository.GetTopAsync(10, weekStart, 2);
        Assert.Equal(2, top.Count);
        Assert.Equal(new ulong[] { 4, 5 }, top.Select(s => s.DiscordUserId).ToArray());
    }

    [Fact]
    public async Task PruneOlderThanAsync_RemovesOnlyStaleScores()
    {
        var oldWeek = new DateTime(2026, 9, 28, 0, 0, 0, DateTimeKind.Utc);
        var currentWeek = new DateTime(2026, 10, 5, 0, 0, 0, DateTimeKind.Utc);

        await _repository.IncrementAsync(6, 10, oldWeek, 1);
        await _repository.IncrementAsync(7, 10, currentWeek, 1);

        var pruned = await _repository.PruneOlderThanAsync(currentWeek);
        Assert.Equal(1, pruned);

        Assert.Empty(await _repository.GetTopAsync(10, oldWeek, 10));
        Assert.Single(await _repository.GetTopAsync(10, currentWeek, 10));
    }
}
