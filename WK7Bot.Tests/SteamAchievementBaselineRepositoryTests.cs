using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using WK7Bot.Core.Entities;
using WK7Bot.Core.Interfaces;
using WK7Bot.Infrastructure.Data;
using Xunit;

namespace WK7Bot.Tests;

public class SteamAchievementBaselineRepositoryTests : IDisposable
{
    private readonly IDbContextFactory<BotDbContext> _factory;
    private readonly ISteamAchievementBaselineRepository _repository;

    public SteamAchievementBaselineRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<BotDbContext>()
            .UseInMemoryDatabase(databaseName: $"steam-achievement-baseline-tests-{Guid.NewGuid()}")
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        _factory = new PooledDbContextFactory<BotDbContext>(options);
        _repository = new SteamAchievementBaselineRepository(_factory);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }

    private static SteamAchievementBaseline CreateBaseline(ulong discordUserId, string steamId, uint appId, string apiName, bool achieved) => new()
    {
        DiscordUserId = discordUserId,
        SteamId = steamId,
        AppId = appId,
        ApiName = apiName,
        Achieved = achieved,
        LastChecked = DateTime.UtcNow
    };

    [Fact]
    public async Task UpsertManyAsync_InsertsAndUpdates()
    {
        await _repository.UpsertManyAsync(new[]
        {
            CreateBaseline(1, "steam1", 10, "ach1", true)
        });

        var loaded = await _repository.GetBaselineAsync(1, "steam1", 10, "ach1");
        Assert.NotNull(loaded);
        Assert.True(loaded!.Achieved);

        await _repository.UpsertManyAsync(new[]
        {
            CreateBaseline(1, "steam1", 10, "ach1", false)
        });

        var updated = await _repository.GetBaselineAsync(1, "steam1", 10, "ach1");
        Assert.NotNull(updated);
        Assert.False(updated!.Achieved);
    }

    [Fact]
    public async Task GetBaselineAsync_FiltersByUserAndSteam()
    {
        await _repository.UpsertManyAsync(new[]
        {
            CreateBaseline(1, "steam1", 10, "ach1", true),
            CreateBaseline(2, "steam2", 20, "ach2", true)
        });

        var results = await _repository.GetBaselineAsync(1, "steam1");
        Assert.Single(results);
        Assert.Equal("ach1", results[0].ApiName);
    }
}
