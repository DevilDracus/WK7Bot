using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using WK7Bot.Core.Entities;
using WK7Bot.Core.Interfaces;
using WK7Bot.Infrastructure.Data;
using Xunit;

namespace WK7Bot.Tests;

public class UserAccountLinkRepositoryTests : IDisposable
{
    private readonly IDbContextFactory<BotDbContext> _factory;
    private readonly IUserAccountLinkRepository _repository;

    public UserAccountLinkRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<BotDbContext>()
            .UseInMemoryDatabase(databaseName: $"account-link-tests-{Guid.NewGuid()}")
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        _factory = new PooledDbContextFactory<BotDbContext>(options);
        _repository = new UserAccountLinkRepository(_factory);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }

    private static UserAccountLink CreateLink(ulong discordUserId, string provider, string externalId) => new()
    {
        DiscordUserId = discordUserId,
        Provider = provider,
        ExternalId = externalId,
        DisplayName = "Original",
        LinkedAt = DateTime.UtcNow
    };

    [Fact]
    public async Task UpsertAsync_InsertsNewLink()
    {
        await _repository.UpsertAsync(CreateLink(1, "steam", "1001"));

        var loaded = await _repository.GetAsync(1, "steam");
        Assert.NotNull(loaded);
        Assert.Equal("1001", loaded!.ExternalId);
        Assert.Equal("Original", loaded.DisplayName);
    }

    [Fact]
    public async Task UpsertAsync_UpdatesExistingLink()
    {
        await _repository.UpsertAsync(CreateLink(2, "battlenet", "bnet#1"));
        await _repository.UpsertAsync(new UserAccountLink
        {
            DiscordUserId = 2,
            Provider = "battlenet",
            ExternalId = "bnet#2",
            DisplayName = "Updated",
            Region = "us",
            LinkedAt = DateTime.UtcNow
        });

        var all = await _repository.GetForUserAsync(2);
        var link = Assert.Single(all);
        Assert.Equal("bnet#2", link.ExternalId);
        Assert.Equal("Updated", link.DisplayName);
        Assert.Equal("us", link.Region);
    }

    [Fact]
    public async Task RemoveAsync_DeletesOnlyMatchingLink()
    {
        await _repository.UpsertAsync(CreateLink(3, "steam", "2001"));
        await _repository.UpsertAsync(CreateLink(3, "battlenet", "2002"));

        Assert.True(await _repository.RemoveAsync(3, "steam"));
        Assert.Null(await _repository.GetAsync(3, "steam"));
        Assert.NotNull(await _repository.GetAsync(3, "battlenet"));
        Assert.False(await _repository.RemoveAsync(3, "steam"));
    }

    [Fact]
    public async Task GetAllAsync_ReturnsAllLinks()
    {
        await _repository.UpsertAsync(CreateLink(4, "steam", "4001"));
        await _repository.UpsertAsync(CreateLink(5, "steam", "5001"));

        var all = await _repository.GetAllAsync();
        Assert.Equal(2, all.Count);
    }
}
