using CodeHollow.FeedReader;
using WK7Bot.Core.Entities;
using WK7Bot.Core.Utilities;

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
    /// When the feed has no baseline markers yet (first poll), the newest item is recorded on the entity
    /// without being returned, so historical backlog is never posted to Discord.
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

            if (sortedItems.Count == 0)
            {
                return newItems;
            }

            var hasBaseline = FeedDeltaCalculator.HasBaseline(feed.LastItemGuid, feed.LastPublishedDate);
            if (!hasBaseline)
            {
                // First poll: seed the baseline from the newest item so the entire
                // feed history is not treated as "new" and spammed to Discord.
                var newest = FeedDeltaCalculator.SelectNewestForBaseline(sortedItems, i => i.PublishingDate);
                if (newest != null)
                {
                    feed.LastItemGuid = newest.Id;
                    feed.LastPublishedDate = newest.PublishingDate;
                }

                return newItems;
            }

            foreach (var item in sortedItems)
            {
                if (FeedDeltaCalculator.IsItemNewer(item.Id, item.PublishingDate, feed.LastItemGuid, feed.LastPublishedDate))
                {
                    newItems.Add(item);
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Shutdown cancellation must not reach the error-DM pipeline; everything else is a
            // genuine feed problem (unreachable URL, malformed XML, timeouts).
            _logger.LogError(ex, "Failed to parse RSS feed '{FeedName}' from URL '{FeedUrl}'.", feed.Name, feed.Url);
        }

        return newItems;
    }
}