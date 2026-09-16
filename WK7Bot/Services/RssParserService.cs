using CodeHollow.FeedReader;
using Microsoft.Extensions.Logging;
using WK7Bot.Core.Entities;

namespace WK7Bot.Services;

/// <summary>
/// Provides functionality for fetching and parsing external RSS and Atom feeds.
/// </summary>
public class RssParserService
{
    private readonly ILogger<RssParserService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="RssParserService"/> class.
    /// </summary>
    /// <param name="logger">The logger instance for diagnostics.</param>
    public RssParserService(ILogger<RssParserService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Fetches the feed from the specified URL and retrieves new items published after the last recorded state.
    /// </summary>
    /// <param name="feed">The database entity containing feed configuration and historical tracking state.</param>
    /// <param name="cancellationToken">Token to monitor for cancellation requests.</param>
    /// <returns>A list of newly published feed items ordered by publication date ascending.</returns>
    public async Task<List<FeedItem>> FetchNewItemsAsync(RssFeed feed, CancellationToken cancellationToken = default)
    {
        var newItems = new List<FeedItem>();

        try
        {
            var parsedFeed = await FeedReader.ReadAsync(feed.Url, cancellationToken);
            var sortedItems = parsedFeed.Items
                .Where(item => item.PublishingDate.HasValue)
                .OrderBy(item => item.PublishingDate)
                .ToList();

            foreach (var item in sortedItems)
            {
                if (IsItemNewer(item, feed))
                {
                    newItems.Add(item);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse RSS feed '{FeedName}' from URL '{FeedUrl}'.", feed.Name, feed.Url);
        }

        return newItems;
    }

    /// <summary>
    /// Determines whether an individual feed item is newer than the recorded feed state.
    /// </summary>
    /// <param name="item">The candidate feed item retrieved from the feed.</param>
    /// <param name="feed">The current feed entity stored in the database.</param>
    /// <returns><see langword="true"/> if the item has not been posted previously; otherwise, <see langword="false"/>.</returns>
    private bool IsItemNewer(FeedItem item, RssFeed feed)
    {
        if (!string.IsNullOrEmpty(feed.LastItemGuid) && item.Id == feed.LastItemGuid)
        {
            return false;
        }

        if (feed.LastPublishedDate.HasValue && item.PublishingDate.HasValue)
        {
            return item.PublishingDate.Value > feed.LastPublishedDate.Value.UtcDateTime;
        }

        return true;
    }
}