namespace WK7Bot.Modules;

using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using WK7Bot.Core.Entities;
using WK7Bot.Core.Interfaces;

/// <summary>
/// Provides slash commands and component interactions for managing RSS feeds, permissions, and self-service subscriptions.
/// </summary>
[Group("rss", "Commands for managing RSS news feeds and subscriptions")]
public class RssCommandsModule : InteractionModuleBase<SocketInteractionContext>
{
    private const string CategoryName = "RSS Feeds";
    private readonly IRssRepository _repository;

    /// <summary>
    /// Initializes a new instance of the <see cref="RssCommandsModule"/> class.
    /// </summary>
    /// <param name="repository">The repository used for RSS feed data persistence.</param>
    public RssCommandsModule(IRssRepository repository)
    {
        _repository = repository;
    }

    /// <summary>
    /// Registers a new RSS feed, creates its category, read-only channel, and notification role, and refreshes the active dashboard.
    /// </summary>
    /// <param name="name">The display name of the feed.</param>
    /// <param name="url">The RSS feed XML endpoint URL.</param>
    /// <returns>A task representing the command response operation.</returns>
    [SlashCommand("add", "Add a new RSS feed to the bot")]
    [RequireUserPermission(GuildPermission.ManageChannels)]
    public async Task AddFeedAsync(
        [Summary("name", "Name of the feed (e.g. PD Leipzig)")] string name,
        [Summary("url", "RSS feed URL")] string url)
    {
        await DeferAsync();

        var guild = Context.Guild;
        var category = await GetOrCreateCategoryAsync(guild, CategoryName);
        var role = await guild.CreateRoleAsync(
            name: $"RSS: {name}",
            permissions: GuildPermissions.None,
            color: Color.Gold,
            isHoisted: false,
            isMentionable: true);

        var channel = await guild.CreateTextChannelAsync(SanitizeChannelName(name), properties =>
        {
            properties.CategoryId = category.Id;
            properties.Topic = $"RSS feed for {name}. Use the subscription dashboard to manage notifications.";
            properties.PermissionOverwrites = new List<Overwrite>
            {
                new(guild.EveryoneRole.Id, PermissionTarget.Role, new OverwritePermissions(sendMessages: PermValue.Deny))
            };
        });

        var feedEntity = new RssFeed
        {
            Name = name,
            Url = url,
            ChannelId = channel.Id,
            RoleId = role.Id
        };

        await _repository.AddFeedAsync(feedEntity);
        await RefreshDashboardMessageAsync();

        await FollowupAsync($"Created read-only channel <#{channel.Id}> and notification role <@&{role.Id}> for RSS feed **{name}**. The subscription dashboard has been updated.");
    }

    /// <summary>
    /// Removes an existing RSS feed and updates the interactive dashboard.
    /// </summary>
    /// <param name="name">The name of the feed to remove.</param>
    /// <returns>A task representing the command response operation.</returns>
    [SlashCommand("remove", "Remove an RSS feed and clean up its associated channel and role")]
    [RequireUserPermission(GuildPermission.ManageChannels)]
    public async Task RemoveFeedAsync([Summary("name", "Name of the feed to remove")] string name)
    {
        await DeferAsync();

        var feed = await _repository.GetFeedByNameAsync(name);
        if (feed == null)
        {
            await FollowupAsync($"No feed named '{name}' was found.", ephemeral: true);
            return;
        }

        var guild = Context.Guild;

        if (guild.GetChannel(feed.ChannelId) is IGuildChannel channel)
        {
            await channel.DeleteAsync();
        }

        if (guild.GetRole(feed.RoleId) is IRole role)
        {
            await role.DeleteAsync();
        }

        await _repository.DeleteFeedAsync(feed.Id);
        await RefreshDashboardMessageAsync();

        await FollowupAsync($"Removed feed **{feed.Name}**, deleted channel, role, and updated the subscription dashboard.");
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

        await RespondAsync(embed: embed, components: component);
        var responseMessage = await GetOriginalResponseAsync();

        await _repository.SaveDashboardLocationAsync(Context.Channel.Id, responseMessage.Id);
    }

    /// <summary>
    /// Handles the selection state changes from the interactive subscription select menu component.
    /// </summary>
    /// <param name="selectedRoleIds">The raw comma-delimited string values submitted by the user from the select menu.</param>
    /// <returns>A task representing the interaction response operation.</returns>
    [ComponentInteraction("rss-subscription-select*")]
    public async Task HandleSubscriptionSelectionAsync(string selectedRoleIds)
    {
        await DeferAsync(ephemeral: true);

        if (Context.User is not SocketGuildUser guildUser)
        {
            await FollowupAsync("This action can only be performed within a server.", ephemeral: true);
            return;
        }

        // Split the values if multiple were selected (Discord sends them comma-separated or via component data)
        var selectedValues = Context.Interaction is SocketMessageComponent socketComponent 
            ? socketComponent.Data.Values 
            : Array.Empty<string>();

        var feeds = await _repository.GetAllFeedsAsync();
        var allFeedRoleIds = feeds.Select(f => f.RoleId).ToHashSet();

        var selectedSet = selectedValues
            .Select(ulong.Parse)
            .ToHashSet();

        var rolesToAdd = selectedSet
            .Where(roleId => !guildUser.Roles.Any(r => r.Id == roleId))
            .ToList();

        var rolesToRemove = allFeedRoleIds
            .Where(roleId => !selectedSet.Contains(roleId) && guildUser.Roles.Any(r => r.Id == roleId))
            .ToList();

        foreach (var roleId in rolesToAdd)
        {
            await guildUser.AddRoleAsync(roleId);
        }

        foreach (var roleId in rolesToRemove)
        {
            await guildUser.RemoveRoleAsync(roleId);
        }

        await FollowupAsync("Your RSS feed subscriptions have been successfully updated!", ephemeral: true);
    }

    /// <summary>
    /// Refreshes the existing subscription dashboard message in Discord with the latest RSS feed options.
    /// </summary>
    /// <returns>A task representing the asynchronous dashboard update operation.</returns>
    private async Task RefreshDashboardMessageAsync()
    {
        var location = await _repository.GetDashboardLocationAsync();
        if (location == null)
        {
            return;
        }

        if (Context.Guild.GetChannel(location.Value.ChannelId) is not ITextChannel channel)
        {
            return;
        }

        if (await channel.GetMessageAsync(location.Value.MessageId) is not IUserMessage message)
        {
            return;
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

            return;
        }

        var (embed, component) = BuildDashboardMessage(feeds);

        await message.ModifyAsync(props =>
        {
            props.Embed = embed;
            props.Components = component;
        });
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

    /// <summary>
    /// Converts a display name into a Discord-compliant channel name string.
    /// </summary>
    /// <param name="rawName">The raw input name.</param>
    /// <returns>A lowercase, hyphenated channel name string.</returns>
    private string SanitizeChannelName(string rawName)
    {
        return rawName.ToLowerInvariant().Replace(' ', '-');
    }
}