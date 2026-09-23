using Discord;
using Discord.WebSocket;
using WK7Bot.Core.Entities;
using WK7Bot.Core.Interfaces;

namespace WK7Bot.Services;

/// <summary>
/// Background service that periodically polls registered RSS feeds and broadcasts updates to Discord.
/// </summary>
public class RssPollingBackgroundService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly DiscordSocketClient _discordClient;
    private readonly ILogger<RssPollingBackgroundService> _logger;
    private bool _isClientReady;

    /// <summary>
    /// Initializes a new instance of the <see cref="RssPollingBackgroundService"/> class.
    /// </summary>
    /// <param name="serviceProvider">The service provider to create database scopes.</param>
    /// <param name="discordClient">The active Discord client instance.</param>
    /// <param name="logger">The logger instance for operational tracking.</param>
    public RssPollingBackgroundService(
        IServiceProvider serviceProvider,
        DiscordSocketClient discordClient,
        ILogger<RssPollingBackgroundService> logger)
    {
        _serviceProvider = serviceProvider;
        _discordClient = discordClient;
        _logger = logger;

        _discordClient.Ready += OnDiscordClientReadyAsync;
    }

    /// <summary>
    /// Handles the internal Ready event emitted by the Discord client, marking the service as ready to broadcast feed updates.
    /// </summary>
    /// <returns>A completed task representing the synchronous state update.</returns>
    private Task OnDiscordClientReadyAsync()
    {
        _isClientReady = true;
        return Task.CompletedTask;
    }

    /// <summary>
    /// Executes the background polling loop while the host service is running.
    /// </summary>
    /// <param name="stoppingToken">Cancellation token indicating service shutdown.</param>
    /// <returns>A task representing the background processing lifecycle.</returns>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (_isClientReady)
                {
                    await PollAllFeedsAsync(stoppingToken);
                }
                else
                {
                    _logger.LogInformation("Discord client is not fully ready. Skipping this RSS polling cycle.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An unhandled exception occurred during RSS polling loop execution.");
            }

            await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
        }
    }

    /// <summary>
    /// Queries all active feeds from the database and processes pending news entries.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token to observe.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    private async Task PollAllFeedsAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var parser = scope.ServiceProvider.GetRequiredService<RssParserService>();
        var repository = scope.ServiceProvider.GetRequiredService<IRssRepository>();

        var feeds = await repository.GetAllFeedsAsync(cancellationToken);

        foreach (var feed in feeds)
        {
            var newItems = await parser.FetchNewItemsAsync(feed, cancellationToken);
            if (!newItems.Any())
            {
                continue;
            }

            if (_discordClient.GetChannel(feed.ChannelId) is ITextChannel channel)
            {
                foreach (var item in newItems)
                {
                    await SendFeedEmbedAsync(channel, feed, item);
                    feed.LastItemGuid = item.Id;
                    feed.LastPublishedDate = item.PublishingDate;
                }

                feed.LastPolledAt = DateTimeOffset.UtcNow;
                await repository.UpdateFeedAsync(feed, cancellationToken);
            }
        }
    }

    /// <summary>
    /// Formats an individual feed entry as a rich Discord embed and sends it with a role mention.
    /// </summary>
    /// <param name="channel">The target Discord channel for the feed.</param>
    /// <param name="feed">The associated feed tracking entity.</param>
    /// <param name="item">The newly retrieved feed item.</param>
    /// <returns>A task representing the send operation.</returns>
    private async Task SendFeedEmbedAsync(ITextChannel channel, RssFeed feed, CodeHollow.FeedReader.FeedItem item)
    {
        // Sanitize the URL to remove any accidental newlines, tabs, or trailing whitespace from the feed parser
        var sanitizedUrl = item.Link?.Trim();

        if (string.IsNullOrWhiteSpace(sanitizedUrl) || !Uri.IsWellFormedUriString(sanitizedUrl, UriKind.Absolute))
        {
            _logger.LogWarning("Skipping feed item with invalid or missing URL for feed {FeedName}.", feed.Name);
            return;
        }
        
        var embedBuilder = new EmbedBuilder()
            .WithTitle(item.Title)
            .WithUrl(sanitizedUrl)
            .WithDescription(CleanDescription(item.Description))
            .WithColor(Color.Blue)
            .WithFooter(text: feed.Name)
            .WithTimestamp(item.PublishingDate ?? DateTimeOffset.UtcNow);

        var mentionText = $"<@&{feed.RoleId}>";
        await channel.SendMessageAsync(text: mentionText, embed: embedBuilder.Build());
    }

    /// <summary>
    /// Strips HTML tags and truncates the feed summary to fit within Discord embed constraints.
    /// </summary>
    /// <param name="rawDescription">The raw HTML or plain text description from the RSS feed.</param>
    /// <returns>A sanitized string suitable for display in a Discord embed description.</returns>
    private string CleanDescription(string rawDescription)
    {
        if (string.IsNullOrWhiteSpace(rawDescription))
        {
            return string.Empty;
        }

        var sanitized = System.Text.RegularExpressions.Regex.Replace(rawDescription, "<.*?>", string.Empty);
        sanitized = System.Net.WebUtility.HtmlDecode(sanitized).Trim();

        return sanitized.Length > 500 ? string.Concat(sanitized.AsSpan(0, 497), "...") : sanitized;
    }
}