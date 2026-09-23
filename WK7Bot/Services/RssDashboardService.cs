using Discord;
using Microsoft.EntityFrameworkCore;
using WK7Bot.Core.Entities;
using WK7Bot.Infrastructure.Data;

namespace WK7Bot.Services;

public class RssDashboardService
{
    private readonly BotDbContext _dbContext;
    private readonly ILogger<RssDashboardService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="RssDashboardService"/> class.
    /// </summary>
    /// <param name="dbContext">The database context for accessing RSS settings and feed records.</param>
    /// <param name="logger">The logger instance for tracking operational events.</param>
    public RssDashboardService(BotDbContext dbContext, ILogger<RssDashboardService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    /// <summary>
    /// Updates the existing RSS subscription dashboard message or creates a new one in the target channel if none exists.
    /// Stores the resulting Channel ID and Message ID in the database.
    /// </summary>
    /// <param name="channel">The text channel where the dashboard embed should reside.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task RefreshDashboardAsync(ITextChannel channel, CancellationToken cancellationToken = default)
    {
        var settings = await _dbContext.RssDashboardSettings
            .OrderBy(s => s.Id)
            .FirstOrDefaultAsync(cancellationToken);

        var feeds = await _dbContext.RssFeeds
            .AsNoTracking()
            .OrderBy(f => f.Name)
            .ToListAsync(cancellationToken);

        var embedBuilder = new EmbedBuilder()
            .WithTitle("📌 RSS Feed Overview")
            .WithColor(Color.Blue)
            .WithTimestamp(DateTimeOffset.UtcNow);

        if (feeds.Count == 0)
        {
            embedBuilder.WithDescription("No active RSS subscriptions found.");
        }
        else
        {
            foreach (var feed in feeds)
            {
                embedBuilder.AddField(
                    feed.Name,
                    $"• **URL:** {feed.Url}\n• **Channel:** <#{feed.ChannelId}>\n• **Role:** <@&{feed.RoleId}>",
                    inline: false);
            }
        }

        var embed = embedBuilder.Build();

        if (settings != null && settings.MessageId != 0)
        {
            try
            {
                var targetMessage = await channel.GetMessageAsync(settings.MessageId) as IUserMessage;
                if (targetMessage != null)
                {
                    await targetMessage.ModifyAsync(msg => msg.Embed = embed);
                    _logger.LogInformation("Successfully updated existing RSS dashboard message {MessageId}.", settings.MessageId);
                    return;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to update existing dashboard message {MessageId}. Creating a new message.", settings.MessageId);
            }
        }

        var newMessage = await channel.SendMessageAsync(embed: embed);

        if (settings == null)
        {
            settings = new RssDashboardSetting
            {
                ChannelId = channel.Id,
                MessageId = newMessage.Id
            };
            _dbContext.RssDashboardSettings.Add(settings);
        }
        else
        {
            settings.ChannelId = channel.Id;
            settings.MessageId = newMessage.Id;
            _dbContext.RssDashboardSettings.Update(settings);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Created new RSS dashboard message {MessageId} in channel {ChannelId}.", newMessage.Id, channel.Id);
    }
}