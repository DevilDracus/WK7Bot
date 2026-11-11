using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using WK7Bot.Core.Interfaces;
using WK7Bot.Infrastructure.Data;
using Xunit;

namespace WK7Bot.Tests;

public class PollenDispatchRepositoryTests : IDisposable
{
    private readonly IDbContextFactory<BotDbContext> _factory;
    private readonly IPollenDispatchRepository _repository;

    public PollenDispatchRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<BotDbContext>()
            .UseInMemoryDatabase(databaseName: $"pollen-tests-{Guid.NewGuid()}")
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        _factory = new PooledDbContextFactory<BotDbContext>(options);
        _repository = new PollenDispatchRepository(_factory);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task UpsertAsync_InsertsState_WhenMissing()
    {
        var date = new DateTime(2026, 10, 5);

        await _repository.UpsertAsync(1, date, "{\"forecast\":1}");

        var state = await _repository.GetLastAsync(1);
        Assert.NotNull(state);
        Assert.Equal(date.Date, state!.LastPostedDate);
        Assert.Equal("{\"forecast\":1}", state.ForecastJson);
    }

    [Fact]
    public async Task UpsertAsync_UpdatesState_WhenExists()
    {
        var firstDate = new DateTime(2026, 10, 5);
        var secondDate = new DateTime(2026, 10, 8);

        await _repository.UpsertAsync(2, firstDate, "{\"forecast\":1}");
        await _repository.UpsertAsync(2, secondDate, "{\"forecast\":2}");

        var state = await _repository.GetLastAsync(2);
        Assert.NotNull(state);
        Assert.Equal(secondDate.Date, state!.LastPostedDate);
        Assert.Equal("{\"forecast\":2}", state.ForecastJson);
    }

    [Fact]
    public async Task GetLastAsync_ReturnsNull_WhenNoState()
    {
        Assert.Null(await _repository.GetLastAsync(999));
    }
}
