using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Options;
using WK7Bot.Core.Entities;
using WK7Bot.Core.Interfaces;
using WK7Bot.Core.Utilities;
using WK7Bot.Options;

namespace WK7Bot.Services;

/// <summary>
/// Background service that periodically polls registered RSS feeds and broadcasts updates to Discord.
/// </summary>
public class RssPollingBackgroundService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly DiscordSocketClient _discordClient;
    private readonly Wk7BotOptions _options;
    private readonly ILogger<RssPollingBackgroundService> _logger;
    private bool _isClientReady;

    /// <summary>Loop tick; individual feeds are polled at their own <see cref="RssFeed.RefreshIntervalMinutes"/>.</summary>
    private static readonly TimeSpan PollTick = TimeSpan.FromMinutes(1);

    /// <summary>Fallback interval when a feed's <see cref="RssFeed.RefreshIntervalMinutes"/> is not positive.</summary>
    private const int DefaultRefreshIntervalMinutes = 15;

    /// <summary>
    /// Initializes a new instance of the <see cref="RssPollingBackgroundService"/> class.
    /// </summary>
    /// <param name="serviceProvider">The service provider to create database scopes.</param>
    /// <param name="discordClient">The active Discord client instance.</param>
    /// <param name="options">The strongly-typed application configuration options.</param>
    /// <param name="logger">The logger instance for operational tracking.</param>
    public RssPollingBackgroundService(
        IServiceProvider serviceProvider,
        DiscordSocketClient discordClient,
        IOptions<Wk7BotOptions> options,
        ILogger<RssPollingBackgroundService> logger)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _discordClient = discordClient ?? throw new ArgumentNullException(nameof(discordClient));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Detaches the gateway subscription on shutdown so the service cannot keep the client referenced
    /// after the host stops it.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token indicating service shutdown.</param>
    /// <returns>A task representing the asynchronous stop operation.</returns>
    public override Task StopAsync(CancellationToken cancellationToken)
    {
        _discordClient.Ready -= OnDiscordClientReadyAsync;
        return base.StopAsync(cancellationToken);
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
        if (!_options.Features.RssPollingEnabled)
        {
            _logger.LogInformation("RSS polling service is disabled via feature options.");
            return;
        }

        _discordClient.Ready += OnDiscordClientReadyAsync;

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
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An unhandled exception occurred during RSS polling loop execution.");
            }

            try
            {
                await Task.Delay(PollTick, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>
    /// Queries all configured feeds and processes those whose refresh interval has elapsed. Each feed
    /// is isolated, so one failing feed cannot abort the sweep for the others.
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
            if (!IsDue(feed))
            {
                continue;
            }

            try
            {
                await PollFeedAsync(parser, repository, feed, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Stamp the attempt anyway so a persistently failing feed is retried at its own
                // interval instead of once per loop tick.
                _logger.LogWarning(ex, "Failed to process feed '{FeedName}'; retrying at its next interval.", feed.Name);

                try
                {
                    feed.LastPolledAt = DateTimeOffset.UtcNow;
                    await repository.UpdateFeedAsync(feed, cancellationToken);
                }
                catch (Exception stampEx)
                {
                    _logger.LogWarning(stampEx, "Could not stamp the failed poll of feed '{FeedName}'.", feed.Name);
                }
            }
        }
    }

    /// <summary>
    /// Determines whether a feed's refresh interval has elapsed since its last poll attempt.
    /// </summary>
    /// <param name="feed">The feed to evaluate.</param>
    /// <returns><see langword="true"/> when the feed should be polled now.</returns>
    private static bool IsDue(RssFeed feed)
    {
        var minutes = feed.RefreshIntervalMinutes > 0 ? feed.RefreshIntervalMinutes : DefaultRefreshIntervalMinutes;
        return !feed.LastPolledAt.HasValue
            || DateTimeOffset.UtcNow - feed.LastPolledAt.Value >= TimeSpan.FromMinutes(minutes);
    }

    /// <summary>
    /// Polls a single feed: fetches new items, posts embeds and persists the advanced baseline.
    /// </summary>
    /// <param name="parser">The RSS parser service used for the fetch.</param>
    /// <param name="repository">The repository used to persist feed state.</param>
    /// <param name="feed">The feed entity to process.</param>
    /// <param name="cancellationToken">Cancellation token to observe.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    private async Task PollFeedAsync(RssParserService parser, IRssRepository repository, RssFeed feed, CancellationToken cancellationToken)
    {
        var newItems = await parser.FetchNewItemsAsync(feed, cancellationToken);

        if (!newItems.Any())
        {
            // Persist the attempt (and a freshly seeded baseline) so the history is never re-posted
            // and a failing feed waits its full interval before the next attempt.
            feed.LastPolledAt = DateTimeOffset.UtcNow;
            await repository.UpdateFeedAsync(feed, cancellationToken);
            return;
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

    /// <summary>
    /// Formats an individual feed entry as a rich Discord embed and sends it with a role mention.
    /// </summary>
    /// <param name="channel">The target Discord channel for the feed.</param>
    /// <param name="feed">The associated feed tracking entity.</param>
    /// <param name="item">The newly retrieved feed item.</param>
    /// <returns>A task representing the send operation.</returns>
    private async Task SendFeedEmbedAsync(ITextChannel channel, RssFeed feed, CodeHollow.FeedReader.FeedItem item)
    {
        // Strip accidental newlines, tabs or trailing whitespace from the feed parser. An item
        // without a usable link is still posted (title + description) — dropping it would lose the
        // entry forever, because the baseline advances past it.
        var sanitizedUrl = item.Link?.Trim();
        var hasValidUrl = !string.IsNullOrWhiteSpace(sanitizedUrl)
            && Uri.IsWellFormedUriString(sanitizedUrl, UriKind.Absolute);

        var embedBuilder = new EmbedBuilder()
            .WithTitle(item.Title);

        if (hasValidUrl)
        {
            embedBuilder = embedBuilder.WithUrl(sanitizedUrl);
        }

        embedBuilder = embedBuilder
            .WithDescription(FeedTextFormatter.SanitizeFeedDescription(item.Description))
            .WithColor(Color.Blue)
            .WithFooter(text: feed.Name)
            .WithTimestamp(item.PublishingDate ?? DateTimeOffset.UtcNow);

        var mentionText = $"<@&{feed.RoleId}>";
        await channel.SendMessageAsync(text: mentionText, embed: embedBuilder.Build());
    }
}