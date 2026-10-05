using Microsoft.EntityFrameworkCore;
using WK7Bot.Core.Entities;
using WK7Bot.Infrastructure.Data;
using Xunit;

namespace WK7Bot.Tests;

public class RssRepositoryTests : IDisposable
{
    private readonly BotDbContext _context;
    private readonly RssRepository _repository;

    public RssRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<BotDbContext>()
            .UseInMemoryDatabase(databaseName: $"rss-tests-{Guid.NewGuid()}")
            .Options;

        _context = new BotDbContext(options);
        _repository = new RssRepository(_context);
    }

    public void Dispose()
    {
        _context.Dispose();
        GC.SuppressFinalize(this);
    }

    private static RssFeed CreateFeed(string name = "Feed", string url = "https://example.com/rss")
        => new() { Name = name, Url = url, ChannelId = 111, RoleId = 222 };

    [Fact]
    public async Task AddFeedAsync_PersistsFeed()
    {
        await _repository.AddFeedAsync(CreateFeed("News"));

        var feeds = await _repository.GetAllFeedsAsync();
        Assert.Single(feeds);
        Assert.Equal("News", feeds[0].Name);
        Assert.True(feeds[0].Id > 0);
    }

    [Fact]
    public async Task GetAllFeedsAsync_ReturnsEmpty_WhenNoFeeds()
    {
        var feeds = await _repository.GetAllFeedsAsync();
        Assert.Empty(feeds);
    }

    [Fact]
    public async Task GetFeedByNameAsync_IsCaseInsensitive()
    {
        await _repository.AddFeedAsync(CreateFeed("PD Leipzig"));

        var found = await _repository.GetFeedByNameAsync("pd leipzig");

        Assert.NotNull(found);
        Assert.Equal("PD Leipzig", found!.Name);
    }

    [Fact]
    public async Task GetFeedByNameAsync_ReturnsNull_WhenMissing()
    {
        Assert.Null(await _repository.GetFeedByNameAsync("does-not-exist"));
    }

    [Fact]
    public async Task UpdateFeedAsync_PersistsChanges()
    {
        var feed = CreateFeed();
        await _repository.AddFeedAsync(feed);

        feed.LastItemGuid = "guid-1";
        feed.LastPublishedDate = DateTimeOffset.UtcNow;
        feed.LastPolledAt = DateTimeOffset.UtcNow;
        await _repository.UpdateFeedAsync(feed);

        var reloaded = await _repository.GetFeedByNameAsync(feed.Name);
        Assert.Equal("guid-1", reloaded!.LastItemGuid);
        Assert.NotNull(reloaded.LastPublishedDate);
        Assert.NotNull(reloaded.LastPolledAt);
    }

    [Fact]
    public async Task DeleteFeedAsync_RemovesFeed()
    {
        var feed = CreateFeed();
        await _repository.AddFeedAsync(feed);

        await _repository.DeleteFeedAsync(feed.Id);

        Assert.Empty(await _repository.GetAllFeedsAsync());
    }

    [Fact]
    public async Task DeleteFeedAsync_UnknownId_DoesNotThrow()
    {
        await _repository.DeleteFeedAsync(99999);
        Assert.Empty(await _repository.GetAllFeedsAsync());
    }

    [Fact]
    public async Task SaveDashboardLocationAsync_CreatesWhenMissing()
    {
        await _repository.SaveDashboardLocationAsync(100, 200);

        var location = await _repository.GetDashboardLocationAsync();
        Assert.NotNull(location);
        Assert.Equal(100ul, location!.Value.ChannelId);
        Assert.Equal(200ul, location.Value.MessageId);
    }

    [Fact]
    public async Task SaveDashboardLocationAsync_UpdatesExisting()
    {
        await _repository.SaveDashboardLocationAsync(100, 200);
        await _repository.SaveDashboardLocationAsync(300, 400);

        var location = await _repository.GetDashboardLocationAsync();
        Assert.Equal(300ul, location!.Value.ChannelId);
        Assert.Equal(400ul, location.Value.MessageId);
    }

    [Fact]
    public async Task GetDashboardLocationAsync_ReturnsNull_WhenNeverSaved()
    {
        Assert.Null(await _repository.GetDashboardLocationAsync());
    }

    [Fact]
    public async Task RssFeed_Defaults_AreSensible()
    {
        var feed = new RssFeed();
        Assert.Equal(string.Empty, feed.Name);
        Assert.Equal(string.Empty, feed.Url);
        Assert.Equal(15, feed.RefreshIntervalMinutes);
        Assert.Null(feed.LastItemGuid);
        Assert.Null(feed.LastPublishedDate);
        Assert.Null(feed.LastPolledAt);
    }
}
