using Microsoft.EntityFrameworkCore;
using WK7Bot.Core.Entities;
using WK7Bot.Core.Interfaces;

namespace WK7Bot.Infrastructure.Data;

/// <summary>
/// Entity Framework Core implementation of the RSS repository data access contract.
/// </summary>
public class RssRepository : IRssRepository
{
    private readonly IDbContextFactory<BotDbContext> _dbContextFactory;

    /// <summary>
    /// Initializes a new instance of the <see cref="RssRepository" /> class.
    /// </summary>
    /// <param name="dbContextFactory">The database context factory.</param>
    public RssRepository(IDbContextFactory<BotDbContext> dbContextFactory)
    {
        _dbContextFactory = dbContextFactory;
    }

    /// <summary>
    /// Retrieves all active RSS feeds registered in the system.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token to observe.</param>
    /// <returns>A collection of all configured RSS feeds.</returns>
    public async Task<List<RssFeed>> GetAllFeedsAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await context.RssFeeds.AsNoTracking().ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Retrieves a specific RSS feed by its display name.
    /// </summary>
    /// <param name="name">The name of the RSS feed.</param>
    /// <param name="cancellationToken">Cancellation token to observe.</param>
    /// <returns>The matching RSS feed entity if found; otherwise, null.</returns>
    public async Task<RssFeed?> GetFeedByNameAsync(string name, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        await using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

        // SQLite's lower() (like COLLATE NOCASE) is ASCII-only, so names containing umlauts or ß
        // would never match; the feed table holds a handful of rows, so compare in memory with
        // ordinal semantics instead. AsNoTracking keeps this consistent with GetAllFeedsAsync —
        // tracking the result here would collide with entities the caller loaded from that method.
        var feeds = await context.RssFeeds.AsNoTracking().ToListAsync(cancellationToken);
        return feeds.FirstOrDefault(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Adds a new RSS feed entity to the storage context.
    /// </summary>
    /// <param name="feed">The feed entity to insert.</param>
    /// <param name="cancellationToken">Cancellation token to observe.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task AddFeedAsync(RssFeed feed, CancellationToken cancellationToken = default)
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        await context.RssFeeds.AddAsync(feed, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Updates an existing RSS feed entity with new state data.
    /// </summary>
    /// <param name="feed">The modified feed entity.</param>
    /// <param name="cancellationToken">Cancellation token to observe.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task UpdateFeedAsync(RssFeed feed, CancellationToken cancellationToken = default)
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        context.RssFeeds.Update(feed);
        await context.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Removes an RSS feed entity by its unique identifier.
    /// </summary>
    /// <param name="id">The database identifier of the feed to remove.</param>
    /// <param name="cancellationToken">Cancellation token to observe.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task DeleteFeedAsync(int id, CancellationToken cancellationToken = default)
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await context.RssFeeds.FindAsync(new object[] { id }, cancellationToken);
        if (entity != null)
        {
            context.RssFeeds.Remove(entity);
            await context.SaveChangesAsync(cancellationToken);
        }
    }

    /// <summary>
    /// Stores or updates the active Discord dashboard message tracking location.
    /// </summary>
    /// <param name="channelId">The channel ID where the dashboard is posted.</param>
    /// <param name="messageId">The message ID of the active dashboard component.</param>
    /// <param name="cancellationToken">Cancellation token to observe.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task SaveDashboardLocationAsync(ulong channelId, ulong messageId, CancellationToken cancellationToken = default)
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var setting = await context.RssDashboardSettings.FirstOrDefaultAsync(cancellationToken);
        if (setting == null)
        {
            setting = new RssDashboardSetting { ChannelId = channelId, MessageId = messageId };
            await context.RssDashboardSettings.AddAsync(setting, cancellationToken);
        }
        else
        {
            setting.ChannelId = channelId;
            setting.MessageId = messageId;
            context.RssDashboardSettings.Update(setting);
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Retrieves the saved dashboard message location if one has been posted.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token to observe.</param>
    /// <returns>A tuple containing the channel ID and message ID, or null if not set.</returns>
    public async Task<(ulong ChannelId, ulong MessageId)?> GetDashboardLocationAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var setting = await context.RssDashboardSettings.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        if (setting == null)
        {
            return null;
        }

        return (setting.ChannelId, setting.MessageId);
    }
}