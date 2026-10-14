using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;
using WK7Bot.Core.Entities;
using WK7Bot.Core.Interfaces;
using WK7Bot.Core.Utilities;

namespace WK7Bot.Modules;

/// <summary>
/// Provides slash commands for managing RSS news feeds: add, list, remove, dashboard, and refresh.
/// </summary>
[Group("rss", "Befehle zur Verwaltung von RSS-Newsfeeds und Abonnements")]
public class RssModule : InteractionModuleBase<SocketInteractionContext>
{
    /// <summary>
    /// Upper bound for feed names: the role becomes "RSS: {name}" (100-char role limit) and the dashboard's
    /// select-menu description "Benachrichtigungen f\u00fcr {name}" must fit into 100 characters.
    /// </summary>
    private const int MaxFeedNameLength = 70;

    internal const string CategoryName = "RSS Feeds";

    /// <summary>
    /// Discord renders at most 25 options (and 25 selected values) in one select menu.
    /// </summary>
    private const int MaxDashboardOptions = 25;

    private readonly IRssRepository _repository;
    private readonly ILogger<RssModule> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="RssModule"/> class.
    /// </summary>
    /// <param name="repository">The repository used for RSS feed data persistence.</param>
    /// <param name="logger">Logger instance used to surface command failures.</param>
    public RssModule(IRssRepository repository, ILogger<RssModule> logger)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Registers a new RSS feed, creates its category, private read-only channel restricted to the notification role, and refreshes the active dashboard.
    /// </summary>
    /// <param name="name">The display name of the feed.</param>
    /// <param name="url">The RSS feed XML endpoint URL.</param>
    /// <returns>A task representing the command response operation.</returns>
    [SlashCommand("add", "F\u00fcgt einen neuen RSS-Feed hinzu")]
    [RequireUserPermission(GuildPermission.ManageChannels)]
    public async Task AddFeedAsync(
        [Summary("name", "Name des Feeds (z. B. PD Leipzig)")] [MaxLength(MaxFeedNameLength)] string name,
        [Summary("url", "RSS-Feed-URL")] [MaxLength(500)] string url)
    {
        await DeferAsync(ephemeral: true);

        name = name.Trim();
        url = url.Trim();

        if (string.IsNullOrWhiteSpace(name) || name.Length > MaxFeedNameLength)
        {
            await FollowupAsync($"\u274c Der Name muss zwischen 1 und {MaxFeedNameLength} Zeichen lang sein.", ephemeral: true);
            return;
        }

        var existing = await _repository.GetFeedByNameAsync(name);
        if (existing != null)
        {
            await FollowupAsync($"\u274c Ein Feed mit dem Namen '{name}' existiert bereits. W\u00e4hle einen anderen Namen oder entferne den vorhandenen Feed zuerst.", ephemeral: true);
            return;
        }

        IRole? createdRole = null;
        ITextChannel? createdChannel = null;
        var feedSaved = false;

        try
        {
            var guild = Context.Guild;

            var baseChannelName = NameSanitizer.ToChannelSlug(name);
            var channelName = await RssChannelProvisioner.FindAvailableChannelNameAsync(guild, baseChannelName);

            var category = await RssChannelProvisioner.GetOrCreateCategoryAsync(guild, CategoryName);
            createdRole = await guild.CreateRoleAsync(
                name: $"RSS: {name}",
                permissions: GuildPermissions.None,
                color: Color.Gold,
                isHoisted: false,
                isMentionable: true);

            createdChannel = await guild.CreateTextChannelAsync(channelName, properties =>
            {
                properties.CategoryId = category.Id;
                properties.Topic = $"RSS-Feed f\u00fcr {name}. Verwalte deine Benachrichtigungen \u00fcber das Abo-Men\u00fc.";
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

            var dashboardUpdated = await TryRefreshDashboardAsync(Context, _repository);
            if (dashboardUpdated)
            {
                await FollowupAsync(
                    $"\u2705 Privater Nur-Lese-Kanal <#{createdChannel.Id}> und Benachrichtigungsrolle <@&{createdRole.Id}> f\u00fcr den RSS-Feed **{name}** erstellt. Das Abo-Men\u00fc wurde aktualisiert.",
                    ephemeral: true);
                return;
            }

            await FollowupAsync(
                $"\u2705 Privater Nur-Lese-Kanal <#{createdChannel.Id}> und Benachrichtigungsrolle <@&{createdRole.Id}> f\u00fcr den RSS-Feed **{name}** erstellt. " +
                "Es ist noch kein Abo-Men\u00fc vorhanden \u2014 erstelle eines mit `/rss dashboard`.",
                ephemeral: true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Adding an RSS feed failed after the interaction was deferred.");

            if (!feedSaved)
            {
                await TryCleanupPartialCreationAsync(createdChannel, createdRole);
                await FollowupAsync(
                    $"\u274c Das Erstellen des Feeds **{name}** ist fehlgeschlagen; teilweise erstellte Kan\u00e4le, Rollen und eine leere Kategorie wurden aufger\u00e4umt. Bitte versuche es erneut.",
                    ephemeral: true);
            }
            else
            {
                await FollowupAsync(
                    $"\u2705 Der Feed **{name}** wurde erstellt, aber das Aktualisieren des Abo-Men\u00fcs ist fehlgeschlagen. F\u00fchre `/rss dashboard` aus, um ein neues zu posten.",
                    ephemeral: true);
            }
        }
    }

    /// <summary>
    /// Lists all registered RSS feeds with their names, URLs, and target channels.
    /// </summary>
    /// <returns>A task representing the command response operation.</returns>
    [SlashCommand("list", "Zeigt alle registrierten RSS-Feeds an")]
    public async Task ListFeedsAsync()
    {
        try
        {
            var feeds = await _repository.GetAllFeedsAsync();

            if (feeds.Count == 0)
            {
                await RespondAsync("Noch keine RSS-Feeds registriert. F\u00fcge einen mit `/rss add` hinzu.", ephemeral: true);
                return;
            }

            var embed = new EmbedBuilder()
                .WithTitle("\ud83d\udce1 RSS-Feeds")
                .WithColor(Color.Blue)
                .WithTimestamp(DateTimeOffset.UtcNow)
                .WithFooter("WK7 Bot \u2022 RSS-Verwaltung");

            foreach (var feed in feeds)
            {
                var channelMention = feed.ChannelId > 0 ? $"<#{feed.ChannelId}>" : "nicht zugewiesen";
                embed.AddField(
                    feed.Name,
                    $"{feed.Url}\nKanal: {channelMention}\nIntervall: {feed.RefreshIntervalMinutes} min",
                    inline: false);
            }

            await RespondAsync(embed: embed.Build(), ephemeral: true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Listing RSS feeds failed.");
            await RespondAsync("\u274c Feeds konnten nicht geladen werden.", ephemeral: true);
        }
    }

    /// <summary>
    /// Removes an existing RSS feed and updates the interactive dashboard.
    /// </summary>
    /// <param name="name">The name of the feed to remove.</param>
    /// <returns>A task representing the command response operation.</returns>
    [SlashCommand("remove", "Entfernt einen RSS-Feed inklusive Kanal und Rolle")]
    [RequireUserPermission(GuildPermission.ManageChannels)]
    public async Task RemoveFeedAsync([Summary("name", "Name des zu entfernenden Feeds")] [MaxLength(MaxFeedNameLength)] string name)
    {
        await DeferAsync(ephemeral: true);

        name = name.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            await FollowupAsync("\u274c Bitte gib den Namen des zu entfernenden Feeds an.", ephemeral: true);
            return;
        }

        var feed = await _repository.GetFeedByNameAsync(name);
        if (feed == null)
        {
            await FollowupAsync($"\u274c Kein Feed mit dem Namen '{name}' gefunden.", ephemeral: true);
            return;
        }

        try
        {
            await _repository.DeleteFeedAsync(feed.Id);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Removing an RSS feed from the database failed.");
            await FollowupAsync($"\u274c Das Entfernen von **{feed.Name}** aus der Datenbank ist fehlgeschlagen; es wurde nichts ge\u00e4ndert. Bitte versuche es erneut.", ephemeral: true);
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
            catch (Exception ex) when (ex is not OperationCanceledException)
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
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                cleanupProblem = true;
                _logger.LogError(ex, "RSS feed {Name} was removed, but its role {RoleId} could not be deleted.", feed.Name, feed.RoleId);
            }
        }

        try
        {
            await TryRefreshDashboardAsync(Context, _repository);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            cleanupProblem = true;
            _logger.LogError(ex, "Subscription dashboard refresh failed after removing RSS feed {Name}.", feed.Name);
        }

        await FollowupAsync(cleanupProblem
            ? $"\u2705 Feed **{feed.Name}** entfernt, aber das Aufr\u00e4umen von Kanal/Rolle oder das Aktualisieren des Abo-Men\u00fcs ist fehlgeschlagen \u2014 bitte pr\u00fcfe den Server."
            : $"\u2705 Feed **{feed.Name}** entfernt, Kanal und Rolle gel\u00f6scht und das Abo-Men\u00fc aktualisiert.",
            ephemeral: true);
    }

    /// <summary>
    /// Posts or updates the interactive subscription dashboard select menu in the current channel and stores its location.
    /// </summary>
    /// <returns>A task representing the command response operation.</returns>
    [SlashCommand("dashboard", "Postet das interaktive RSS-Abo-Men\u00fc")]
    [RequireUserPermission(GuildPermission.ManageRoles)]
    public async Task PostDashboardAsync()
    {
        var responseSent = false;
        try
        {
            var feeds = await _repository.GetAllFeedsAsync();

            if (!feeds.Any())
            {
                await RespondAsync("\u274c Es sind noch keine RSS-Feeds zum Abonnieren vorhanden.", ephemeral: true);
                return;
            }

            var (embed, component) = BuildDashboardMessage(feeds);

            await RespondAsync(embed: embed, components: component);
            responseSent = true;

            var responseMessage = await GetOriginalResponseAsync();
            await _repository.SaveDashboardLocationAsync(Context.Channel.Id, responseMessage.Id);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Posting the RSS subscription dashboard failed.");

            var message = responseSent
                ? "\u26a0\ufe0f Das Abo-Men\u00fc wurde gepostet, aber seine Position konnte nicht gespeichert werden \u2014 automatische Aktualisierungen funktionieren erst, nachdem es erneut gepostet wurde."
                : "\u274c Das Abo-Men\u00fc konnte nicht erstellt werden. Bitte versuche es sp\u00e4ter erneut.";

            if (responseSent)
            {
                await FollowupAsync(message, ephemeral: true);
            }
            else
            {
                await RespondAsync(message, ephemeral: true);
            }
        }
    }

    /// <summary>
    /// Refreshes the stored subscription dashboard message with the latest RSS feed options.
    /// </summary>
    /// <param name="context">The interaction context that identifies the guild to look the stored message up in.</param>
    /// <param name="repository">The RSS repository holding the dashboard location and feeds.</param>
    /// <returns>
    /// A task containing <see langword="true"/> when the stored dashboard message was updated, or
    /// <see langword="false"/> when no (longer fetchable) dashboard exists.
    /// </returns>
    internal static async Task<bool> TryRefreshDashboardAsync(SocketInteractionContext context, IRssRepository repository)
    {
        var location = await repository.GetDashboardLocationAsync();
        if (location == null)
        {
            return false;
        }

        if (context.Guild.GetChannel(location.Value.ChannelId) is not ITextChannel channel)
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
            return false;
        }

        if (fetchedMessage is not IUserMessage message)
        {
            return false;
        }

        var feeds = await repository.GetAllFeedsAsync();
        if (!feeds.Any())
        {
            var emptyEmbed = new EmbedBuilder()
                .WithTitle("RSS-Feed-Abos")
                .WithDescription("Aktuell sind keine RSS-Feeds zum Abonnieren vorhanden.")
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
    /// Constructs the Discord embed and select menu component for the RSS dashboard.
    /// </summary>
    /// <param name="feeds">The list of active RSS feeds.</param>
    /// <returns>A tuple containing the constructed embed and message components.</returns>
    private static (Embed Embed, MessageComponent Component) BuildDashboardMessage(List<RssFeed> feeds)
    {
        var shownFeeds = feeds.Take(MaxDashboardOptions).ToList();
        var hiddenCount = feeds.Count - shownFeeds.Count;

        var menuBuilder = new SelectMenuBuilder()
            .WithCustomId("rss-subscription-select")
            .WithPlaceholder("W\u00e4hle die RSS-Feeds aus, die du abonnieren m\u00f6chtest...")
            .WithMinValues(0)
            .WithMaxValues(shownFeeds.Count);

        foreach (var feed in shownFeeds)
        {
            menuBuilder.AddOption(
                label: feed.Name,
                value: feed.RoleId.ToString(),
                description: $"Benachrichtigungen f\u00fcr {feed.Name}");
        }

        var component = new ComponentBuilder()
            .WithSelectMenu(menuBuilder)
            .Build();

        var description = "W\u00e4hle im Men\u00fc unten aus, welche lokalen Nachrichten und Polizeimeldungen du abonnieren m\u00f6chtest. Die Auswahl wirkt sich sofort auf deine Benachrichtigungsrollen aus.";
        if (hiddenCount > 0)
        {
            description += $"\n\n\u26a0\ufe0f {hiddenCount} weitere Feeds sind eingerichtet, zeigt Discord in diesem Men\u00fc aus Platzgr\u00fcnden nicht an.";
        }

        var embed = new EmbedBuilder()
            .WithTitle("RSS-Feed-Abos")
            .WithDescription(description)
            .WithColor(Color.Blue)
            .Build();

        return (embed, component);
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
            catch (Exception ex) when (ex is not OperationCanceledException)
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
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Rollback of a failed RSS feed creation could not delete role {RoleId}.", role.Id);
            }
        }

        try
        {
            if (Context.Guild is not IGuild guild)
            {
                return;
            }

            var categories = await guild.GetCategoriesAsync();
            var category = categories.FirstOrDefault(c => c.Name.Equals(CategoryName, StringComparison.OrdinalIgnoreCase));
            if (category == null)
            {
                return;
            }

            var channels = await guild.GetTextChannelsAsync();
            if (!channels.Any(c => c.CategoryId == category.Id))
            {
                await category.DeleteAsync();
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Rollback of a failed RSS feed creation could not delete the empty category.");
        }
    }
}
