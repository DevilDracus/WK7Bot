namespace WK7Bot.Core.Entities;

/// <summary>
/// Represents an RSS feed source tracked by the bot alongside its associated Discord channel and notification role.
/// </summary>
public class RssFeed
{
    /// <summary>
    /// Gets or sets the unique database identifier for the RSS feed.
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Gets or sets the user-friendly name of the feed.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the target RSS or Atom feed URL.
    /// </summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the Discord channel ID dedicated to this feed.
    /// </summary>
    public ulong ChannelId { get; set; }

    /// <summary>
    /// Gets or sets the Discord role ID used for subscriber notifications.
    /// </summary>
    public ulong RoleId { get; set; }

    /// <summary>
    /// Gets or sets the identifier of the last processed feed item to prevent duplicate postings.
    /// </summary>
    public string? LastItemGuid { get; set; }

    /// <summary>
    /// Gets or sets the publication timestamp of the last processed item.
    /// </summary>
    public DateTimeOffset? LastPublishedDate { get; set; }

    /// <summary>
    /// Gets or sets the polling interval in minutes.
    /// </summary>
    public int RefreshIntervalMinutes { get; set; } = 15;

    /// <summary>
    /// Gets or sets the timestamp when the feed was last polled.
    /// </summary>
    public DateTimeOffset? LastPolledAt { get; set; }
}