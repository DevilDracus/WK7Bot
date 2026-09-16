namespace WK7Bot.Infrastructure.Data;

using Microsoft.EntityFrameworkCore;
using WK7Bot.Core.Entities;
using WK7Bot.Core.Interfaces;

/// <summary>
/// Entity Framework Core implementation of the RSS repository data access contract.
/// </summary>
public class RssRepository : IRssRepository
{
    private readonly BotDbContext _dbContext;

    /// <summary>
    /// Initializes a new instance of the <see cref="RssRepository"/> class.
    /// </summary>
    /// <param name="dbContext">The database context instance.</param>
    public RssRepository(BotDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <summary>
    /// Retrieves all active RSS feeds registered in the system.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token to observe.</param>
    /// <returns>A collection of all configured RSS feeds.</returns>
    public async Task<List<RssFeed>> GetAllFeedsAsync(CancellationToken cancellationToken = default)
    {
        return await _dbContext.RssFeeds.AsNoTracking().ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Retrieves a specific RSS feed by its display name.
    /// </summary>
    /// <param name="name">The name of the RSS feed.</param>
    /// <param name="cancellationToken">Cancellation token to observe.</param>
    /// <returns>The matching RSS feed entity if found; otherwise, null.</returns>
    public async Task<RssFeed?> GetFeedByNameAsync(string name, CancellationToken cancellationToken = default)
    {
        return await _dbContext.RssFeeds
            .FirstOrDefaultAsync(f => f.Name.ToLower() == name.ToLower(), cancellationToken);
    }

    /// <summary>
    /// Adds a new RSS feed entity to the storage context.
    /// </summary>
    /// <param name="feed">The feed entity to insert.</param>
    /// <param name="cancellationToken">Cancellation token to observe.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task AddFeedAsync(RssFeed feed, CancellationToken cancellationToken = default)
    {
        await _dbContext.RssFeeds.AddAsync(feed, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Updates an existing RSS feed entity with new state data.
    /// </summary>
    /// <param name="feed">The modified feed entity.</param>
    /// <param name="cancellationToken">Cancellation token to observe.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task UpdateFeedAsync(RssFeed feed, CancellationToken cancellationToken = default)
    {
        _dbContext.RssFeeds.Update(feed);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Removes an RSS feed entity by its unique identifier.
    /// </summary>
    /// <param name="id">The database identifier of the feed to remove.</param>
    /// <param name="cancellationToken">Cancellation token to observe.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task DeleteFeedAsync(int id, CancellationToken cancellationToken = default)
    {
        var entity = await _dbContext.RssFeeds.FindAsync(new object[] { id }, cancellationToken);
        if (entity != null)
        {
            _dbContext.RssFeeds.Remove(entity);
            await _dbContext.SaveChangesAsync(cancellationToken);
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
        var setting = await _dbContext.DashboardSettings.FirstOrDefaultAsync(cancellationToken);
        if (setting == null)
        {
            setting = new RssDashboardSetting { ChannelId = channelId, MessageId = messageId };
            await _dbContext.DashboardSettings.AddAsync(setting, cancellationToken);
        }
        else
        {
            setting.ChannelId = channelId;
            setting.MessageId = messageId;
            _dbContext.DashboardSettings.Update(setting);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Retrieves the saved dashboard message location if one has been posted.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token to observe.</param>
    /// <returns>A tuple containing the channel ID and message ID, or null if not set.</returns>
    public async Task<(ulong ChannelId, ulong MessageId)?> GetDashboardLocationAsync(CancellationToken cancellationToken = default)
    {
        var setting = await _dbContext.DashboardSettings.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        if (setting == null)
        {
            return null;
        }

        return (setting.ChannelId, setting.MessageId);
    }
}