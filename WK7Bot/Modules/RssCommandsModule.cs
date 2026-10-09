using Discord;
using Discord.Interactions;
using Microsoft.Extensions.Logging;
using WK7Bot.Core.Entities;
using WK7Bot.Core.Interfaces;
using WK7Bot.Core.Utilities;

namespace WK7Bot.Modules;

/// <summary>
/// Provides slash commands for managing RSS news feeds and posting subscription dashboards.
/// </summary>
[Group("rss", "Commands for managing RSS news feeds and subscriptions")]
public class RssCommandsModule : InteractionModuleBase<SocketInteractionContext>
{
    private const string CategoryName = "RSS Feeds";

    /// <summary>
    /// Upper bound for feed names: the role becomes "RSS: {name}" (100-char role limit) and the dashboard's
    /// select-menu description "Receive notifications for {name}" must fit into 100 characters.
    /// </summary>
    private const int MaxFeedNameLength = 70;

    private readonly IRssRepository _repository;
    private readonly ILogger<RssCommandsModule> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="RssCommandsModule"/> class.
    /// </summary>
    /// <param name="repository">The repository used for RSS feed data persistence.</param>
    /// <param name="logger">Logger instance used to surface command failures.</param>
    public RssCommandsModule(IRssRepository repository, ILogger<RssCommandsModule> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    /// <summary>
    /// Registers a new RSS feed, creates its category, private read-only channel restricted to the notification role, and refreshes the active dashboard.
    /// </summary>
    /// <param name="name">The display name of the feed.</param>
    /// <param name="url">The RSS feed XML endpoint URL.</param>
    /// <returns>A task representing the command response operation.</returns>
    [SlashCommand("add", "Add a new RSS feed to the bot")]
    [RequireUserPermission(GuildPermission.ManageChannels)]
    public async Task AddFeedAsync(
        [Summary("name", "Name of the feed (e.g. PD Leipzig)")] [MaxLength(MaxFeedNameLength)] string name,
        [Summary("url", "RSS feed URL")] [MaxLength(500)] string url)
    {
        await DeferAsync();

        name = name.Trim();
        url = url.Trim();

        var existing = await _repository.GetFeedByNameAsync(name);
        if (existing != null)
        {
            await FollowupAsync($"A feed named '{name}' already exists. Choose a different name or remove the existing feed first.", ephemeral: true);
            return;
        }

        IRole? createdRole = null;
        ITextChannel? createdChannel = null;
        var feedSaved = false;

        try
        {
            var guild = Context.Guild;
            var category = await GetOrCreateCategoryAsync(guild, CategoryName);
            createdRole = await guild.CreateRoleAsync(
                name: $"RSS: {name}",
                permissions: GuildPermissions.None,
                color: Color.Gold,
                isHoisted: false,
                isMentionable: true);

            createdChannel = await guild.CreateTextChannelAsync(NameSanitizer.ToChannelSlug(name), properties =>
            {
                properties.CategoryId = category.Id;
                properties.Topic = $"RSS feed for {name}. Use the subscription dashboard to manage notifications.";
                properties.PermissionOverwrites = new List<Overwrite>
                {
                    new(guild.EveryoneRole.Id, PermissionTarget.Role, new OverwritePermissions(viewChannel: PermValue.Deny)),
                    new(createdRole.Id, PermissionTarget.Role, new OverwritePermissions(viewChannel: PermValue.Allow, readMessageHistory: PermValue.Allow, sendMessages: PermValue.Deny))
                };
            });

            var feedEntity = new RssFeed
            {
                Name = name,
                Url = url,
                ChannelId = createdChannel.Id,
                RoleId = createdRole.Id
            };

            await _repository.AddFeedAsync(feedEntity);
            feedSaved = true;

            var dashboardUpdated = await RefreshDashboardMessageAsync();

            await FollowupAsync(
                $"Created private read-only channel <#{createdChannel.Id}> and notification role <@&{createdRole.Id}> for RSS feed **{name}**. " +
                (dashboardUpdated
                    ? "The subscription dashboard has been updated."
                    : "No dashboard is posted yet — use `/rss dashboard` to create one."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Adding an RSS feed failed after the interaction was deferred.");

            if (!feedSaved)
            {
                await TryCleanupPartialCreationAsync(createdChannel, createdRole);
                await FollowupAsync(
                    $"Creating the feed **{name}** failed and any partially created channel or role was cleaned up. Please try again.",
                    ephemeral: true);
            }
            else
            {
                await FollowupAsync(
                    $"The feed **{name}** was created, but refreshing the subscription dashboard failed. Run `/rss dashboard` to post a fresh one.",
                    ephemeral: true);
            }
        }
    }

    /// <summary>
    /// Removes an existing RSS feed and updates the interactive dashboard.
    /// </summary>
    /// <param name="name">The name of the feed to remove.</param>
    /// <returns>A task representing the command response operation.</returns>
    [SlashCommand("remove", "Remove an RSS feed and clean up its associated channel and role")]
    [RequireUserPermission(GuildPermission.ManageChannels)]
    public async Task RemoveFeedAsync([Summary("name", "Name of the feed to remove")] [MaxLength(100)] string name)
    {
        await DeferAsync();

        name = name.Trim();
        var feed = await _repository.GetFeedByNameAsync(name);
        if (feed == null)
        {
            await FollowupAsync($"No feed named '{name}' was found.", ephemeral: true);
            return;
        }

        try
        {
            await _repository.DeleteFeedAsync(feed.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Removing an RSS feed from the database failed.");
            await FollowupAsync($"Removing **{feed.Name}** from the database failed; nothing was changed. Please try again.", ephemeral: true);
            return;
        }

        var cleanupProblem = false;
        var guild = Context.Guild;

        if (guild.GetChannel(feed.ChannelId) is IGuildChannel channel)
        {
            try
            {
                await channel.DeleteAsync();
            }
            catch (Exception ex)
            {
                cleanupProblem = true;
                _logger.LogError(ex, "RSS feed {Name} was removed, but its channel {ChannelId} could not be deleted.", feed.Name, feed.ChannelId);
            }
        }

        if (guild.GetRole(feed.RoleId) is IRole role)
        {
            try
            {
                await role.DeleteAsync();
            }
            catch (Exception ex)
            {
                cleanupProblem = true;
                _logger.LogError(ex, "RSS feed {Name} was removed, but its role {RoleId} could not be deleted.", feed.Name, feed.RoleId);
            }
        }

        try
        {
            await RefreshDashboardMessageAsync();
        }
        catch (Exception ex)
        {
            cleanupProblem = true;
            _logger.LogError(ex, "Subscription dashboard refresh failed after removing RSS feed {Name}.", feed.Name);
        }

        await FollowupAsync(cleanupProblem
            ? $"Removed feed **{feed.Name}**, but cleaning up its channel/role or refreshing the dashboard hit an error — please check the server."
            : $"Removed feed **{feed.Name}**, deleted channel, role, and updated the subscription dashboard.");
    }

    /// <summary>
    /// Posts or updates the interactive subscription dashboard select menu in the current channel and stores its location.
    /// </summary>
    /// <returns>A task representing the command response operation.</returns>
    [SlashCommand("dashboard", "Post the interactive RSS subscription selection dashboard")]
    [RequireUserPermission(GuildPermission.ManageRoles)]
    public async Task PostDashboardAsync()
    {
        var feeds = await _repository.GetAllFeedsAsync();
        if (!feeds.Any())
        {
            await RespondAsync("No RSS feeds are available for subscription.", ephemeral: true);
            return;
        }

        var (embed, component) = BuildDashboardMessage(feeds);

        // The dashboard must NOT be ephemeral: RefreshDashboardMessageAsync fetches it by channel/message ID,
        // which is impossible for ephemeral messages, so the stored location would be dead on arrival.
        await RespondAsync(embed: embed, components: component);
        var responseMessage = await GetOriginalResponseAsync();

        try
        {
            await _repository.SaveDashboardLocationAsync(Context.Channel.Id, responseMessage.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Persisting the RSS dashboard location failed after the message was posted.");
            await FollowupAsync(
                "The dashboard was posted, but its location could not be saved, so automatic refreshes will not work until it is posted again.",
                ephemeral: true);
        }
    }

    /// <summary>
    /// Refreshes the existing subscription dashboard message in Discord with the latest RSS feed options.
    /// </summary>
    /// <returns>A task containing <see langword="true"/> when the stored dashboard message was updated, or <see langword="false"/> when no (longer fetchable) dashboard exists.</returns>
    private async Task<bool> RefreshDashboardMessageAsync()
    {
        var location = await _repository.GetDashboardLocationAsync();
        if (location == null)
        {
            return false;
        }

        if (Context.Guild.GetChannel(location.Value.ChannelId) is not ITextChannel channel)
        {
            return false;
        }

        IMessage fetchedMessage;
        try
        {
            fetchedMessage = await channel.GetMessageAsync(location.Value.MessageId);
        }
        catch (Discord.Net.HttpException ex) when (ex.HttpCode == System.Net.HttpStatusCode.NotFound)
        {
            // The stored dashboard message was deleted; there is nothing left to refresh.
            return false;
        }

        if (fetchedMessage is not IUserMessage message)
        {
            return false;
        }

        var feeds = await _repository.GetAllFeedsAsync();
        if (!feeds.Any())
        {
            var emptyEmbed = new EmbedBuilder()
                .WithTitle("RSS Feed Subscriptions")
                .WithDescription("No RSS feeds are currently available for subscription.")
                .WithColor(Color.DarkGrey)
                .Build();

            await message.ModifyAsync(props =>
            {
                props.Embed = emptyEmbed;
                props.Components = new ComponentBuilder().Build();
            });

            return true;
        }

        var (embed, component) = BuildDashboardMessage(feeds);

        await message.ModifyAsync(props =>
        {
            props.Embed = embed;
            props.Components = component;
        });

        return true;
    }

    /// <summary>
    /// Best-effort rollback of a partially created RSS feed: deletes the channel and role that were created
    /// before persistence failed, logging (and therefore surfacing) anything that cannot be cleaned up.
    /// </summary>
    /// <param name="channel">The channel created during the failed attempt, if any.</param>
    /// <param name="role">The role created during the failed attempt, if any.</param>
    /// <returns>A task representing the cleanup attempts.</returns>
    private async Task TryCleanupPartialCreationAsync(ITextChannel? channel, IRole? role)
    {
        if (channel != null)
        {
            try
            {
                await channel.DeleteAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Rollback of a failed RSS feed creation could not delete channel {ChannelId}.", channel.Id);
            }
        }

        if (role != null)
        {
            try
            {
                await role.DeleteAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Rollback of a failed RSS feed creation could not delete role {RoleId}.", role.Id);
            }
        }
    }

    /// <summary>
    /// Constructs the Discord embed and select menu component for the RSS dashboard.
    /// </summary>
    /// <param name="feeds">The list of active RSS feeds.</param>
    /// <returns>A tuple containing the constructed embed and message components.</returns>
    private (Embed Embed, MessageComponent Component) BuildDashboardMessage(List<RssFeed> feeds)
    {
        var menuBuilder = new SelectMenuBuilder()
            .WithCustomId("rss-subscription-select")
            .WithPlaceholder("Select RSS feeds to subscribe to...")
            .WithMinValues(0)
            .WithMaxValues(feeds.Count);

        foreach (var feed in feeds)
        {
            menuBuilder.AddOption(
                label: feed.Name,
                value: feed.RoleId.ToString(),
                description: $"Receive notifications for {feed.Name}");
        }

        var component = new ComponentBuilder()
            .WithSelectMenu(menuBuilder)
            .Build();

        var embed = new EmbedBuilder()
            .WithTitle("RSS Feed Subscriptions")
            .WithDescription("Choose which local news and police feeds you want to subscribe to using the dropdown menu below. Selecting or deselecting options updates your notification roles immediately.")
            .WithColor(Color.Blue)
            .Build();

        return (embed, component);
    }

    /// <summary>
    /// Fetches the existing Discord category or creates a new one if it does not exist.
    /// </summary>
    /// <param name="guild">Target guild where the category should exist.</param>
    /// <param name="categoryName">Name of the parent category.</param>
    /// <returns>The existing or newly created category channel.</returns>
    private async Task<ICategoryChannel> GetOrCreateCategoryAsync(IGuild guild, string categoryName)
    {
        var categories = await guild.GetCategoriesAsync();
        var existingCategory = categories.FirstOrDefault(c => c.Name.Equals(categoryName, StringComparison.OrdinalIgnoreCase));

        if (existingCategory != null)
        {
            return existingCategory;
        }

        return await guild.CreateCategoryAsync(categoryName);
    }
}