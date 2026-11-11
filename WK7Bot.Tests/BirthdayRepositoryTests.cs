using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using WK7Bot.Core.Entities;
using WK7Bot.Core.Interfaces;
using WK7Bot.Infrastructure.Data;
using Xunit;

namespace WK7Bot.Tests;

public class BirthdayRepositoryTests : IDisposable
{
    private readonly IDbContextFactory<BotDbContext> _factory;
    private readonly IBirthdayRepository _repository;

    public BirthdayRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<BotDbContext>()
            .UseInMemoryDatabase(databaseName: $"birthday-tests-{Guid.NewGuid()}")
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        _factory = new PooledDbContextFactory<BotDbContext>(options);
        _repository = new BirthdayRepository(_factory);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task UpsertAsync_InsertsNewBirthday()
    {
        await _repository.UpsertAsync(new Birthday
        {
            DiscordUserId = 1,
            GuildId = 10,
            Month = 5,
            Day = 15
        });

        var loaded = await _repository.GetForUserAsync(1);
        Assert.NotNull(loaded);
        Assert.Equal(5, loaded!.Month);
        Assert.Equal(15, loaded.Day);
        Assert.Equal(10ul, loaded.GuildId);
    }

    [Fact]
    public async Task UpsertAsync_UpdatesExistingBirthday()
    {
        await _repository.UpsertAsync(new Birthday
        {
            DiscordUserId = 2,
            GuildId = 20,
            Month = 1,
            Day = 1
        });

        await _repository.UpsertAsync(new Birthday
        {
            DiscordUserId = 2,
            GuildId = 21,
            Month = 12,
            Day = 31
        });

        var loaded = await _repository.GetForUserAsync(2);
        Assert.NotNull(loaded);
        Assert.Equal(12, loaded!.Month);
        Assert.Equal(31, loaded.Day);
        Assert.Equal(21ul, loaded.GuildId);
    }

    [Fact]
    public async Task GetDueTodayAsync_ReturnsOnlyUnannouncedMatches()
    {
        await _repository.UpsertAsync(new Birthday { DiscordUserId = 3, Month = 4, Day = 10 });
        await _repository.UpsertAsync(new Birthday { DiscordUserId = 4, Month = 4, Day = 10 });

        var all = await _repository.GetDueTodayAsync(4, 10, 2026);
        Assert.Equal(2, all.Count);

        var birthday = Assert.Single(all, b => b.DiscordUserId == 3);
        await _repository.MarkAnnouncedAsync(birthday.Id, 2026);

        var remaining = await _repository.GetDueTodayAsync(4, 10, 2026);
        Assert.Single(remaining);
        Assert.Equal(4ul, remaining[0].DiscordUserId);
    }

    [Fact]
    public async Task RemoveAsync_DeletesBirthday()
    {
        await _repository.UpsertAsync(new Birthday { DiscordUserId = 5, Month = 7, Day = 20 });

        Assert.True(await _repository.RemoveAsync(5));
        Assert.Null(await _repository.GetForUserAsync(5));
        Assert.False(await _repository.RemoveAsync(5));
    }
}
