using Discord;
using Discord.Interactions;
using Microsoft.Extensions.Logging;
using WK7Bot.Core.Entities;
using WK7Bot.Core.Interfaces;
using WK7Bot.Core.Utilities;

namespace WK7Bot.Modules;

/// <summary>
/// Provides slash commands for managing RSS news feeds: add, list, and remove.
/// The subscription dashboard itself lives in <see cref="RssDashboardModule"/>.
/// </summary>
public class RssFeedManagementModule : RssModuleBase
{
    /// <summary>
    /// Upper bound for feed names: the role becomes "RSS: {name}" (100-char role limit) and the dashboard's
    /// select-menu description "Benachrichtigungen für {name}" must fit into 100 characters.
    /// </summary>
    private const int MaxFeedNameLength = 70;

    private readonly IRssRepository _repository;
    private readonly ILogger<RssFeedManagementModule> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="RssFeedManagementModule"/> class.
    /// </summary>
    /// <param name="repository">The repository used for RSS feed data persistence.</param>
    /// <param name="logger">Logger instance used to surface command failures.</param>
    public RssFeedManagementModule(IRssRepository repository, ILogger<RssFeedManagementModule> logger)
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
    [SlashCommand("add", "Fügt einen neuen RSS-Feed hinzu")]
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
            await FollowupAsync($"❌ Der Name muss zwischen 1 und {MaxFeedNameLength} Zeichen lang sein.", ephemeral: true);
            return;
        }

        var existing = await _repository.GetFeedByNameAsync(name);
        if (existing != null)
        {
            await FollowupAsync($"❌ Ein Feed mit dem Namen '{name}' existiert bereits. Wähle einen anderen Namen oder entferne den vorhandenen Feed zuerst.", ephemeral: true);
            return;
        }

        IRole? createdRole = null;
        ITextChannel? createdChannel = null;
        var feedSaved = false;

        try
        {
            var guild = Context.Guild;

            // Slugging strips punctuation, so two different feed names can sanitize to the same
            // channel name; append a counter to keep the channels distinguishable.
            var baseChannelName = NameSanitizer.ToChannelSlug(name);
            var channelName = await RssChannelProvisioner.FindAvailableChannelNameAsync(guild, baseChannelName);

            var category = await RssChannelProvisioner.GetOrCreateCategoryAsync(guild, RssDashboardModule.CategoryName);
            createdRole = await guild.CreateRoleAsync(
                name: $"RSS: {name}",
                permissions: GuildPermissions.None,
                color: Color.Gold,
                isHoisted: false,
                isMentionable: true);

            createdChannel = await guild.CreateTextChannelAsync(channelName, properties =>
            {
                properties.CategoryId = category.Id;
                properties.Topic = $"RSS-Feed für {name}. Verwalte deine Benachrichtigungen über das Abo-Menü.";
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

            var dashboardUpdated = await RssDashboardModule.TryRefreshDashboardAsync(Context, _repository);
            if (dashboardUpdated)
            {
                await FollowupAsync(
                    $"✅ Privater Nur-Lese-Kanal <#{createdChannel.Id}> und Benachrichtigungsrolle <@&{createdRole.Id}> für den RSS-Feed **{name}** erstellt. Das Abo-Menü wurde aktualisiert.",
                    ephemeral: true);
                return;
            }

            await FollowupAsync(
                $"✅ Privater Nur-Lese-Kanal <#{createdChannel.Id}> und Benachrichtigungsrolle <@&{createdRole.Id}> für den RSS-Feed **{name}** erstellt. " +
                "Es ist noch kein Abo-Menü vorhanden — erstelle eines mit `/rss dashboard`.",
                ephemeral: true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Adding an RSS feed failed after the interaction was deferred.");

            if (!feedSaved)
            {
                await TryCleanupPartialCreationAsync(createdChannel, createdRole);
                await FollowupAsync(
                    $"❌ Das Erstellen des Feeds **{name}** ist fehlgeschlagen; teilweise erstellte Kanäle, Rollen und eine leere Kategorie wurden aufgeräumt. Bitte versuche es erneut.",
                    ephemeral: true);
            }
            else
            {
                await FollowupAsync(
                    $"✅ Der Feed **{name}** wurde erstellt, aber das Aktualisieren des Abo-Menüs ist fehlgeschlagen. Führe `/rss dashboard` aus, um ein neues zu posten.",
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
                await RespondAsync("Noch keine RSS-Feeds registriert. Füge einen mit `/rss add` hinzu.", ephemeral: true);
                return;
            }

            var embed = new EmbedBuilder()
                .WithTitle("📡 RSS-Feeds")
                .WithColor(Color.Blue)
                .WithTimestamp(DateTimeOffset.UtcNow)
                .WithFooter("WK7 Bot • RSS-Verwaltung");

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
            await RespondAsync("❌ Feeds konnten nicht geladen werden.", ephemeral: true);
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
            await FollowupAsync("❌ Bitte gib den Namen des zu entfernenden Feeds an.", ephemeral: true);
            return;
        }

        var feed = await _repository.GetFeedByNameAsync(name);
        if (feed == null)
        {
            await FollowupAsync($"❌ Kein Feed mit dem Namen '{name}' gefunden.", ephemeral: true);
            return;
        }

        try
        {
            await _repository.DeleteFeedAsync(feed.Id);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Removing an RSS feed from the database failed.");
            await FollowupAsync($"❌ Das Entfernen von **{feed.Name}** aus der Datenbank ist fehlgeschlagen; es wurde nichts geändert. Bitte versuche es erneut.", ephemeral: true);
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
            await RssDashboardModule.TryRefreshDashboardAsync(Context, _repository);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            cleanupProblem = true;
            _logger.LogError(ex, "Subscription dashboard refresh failed after removing RSS feed {Name}.", feed.Name);
        }

        await FollowupAsync(cleanupProblem
            ? $"✅ Feed **{feed.Name}** entfernt, aber das Aufräumen von Kanal/Rolle oder das Aktualisieren des Abo-Menüs ist fehlgeschlagen — bitte prüfe den Server."
            : $"✅ Feed **{feed.Name}** entfernt, Kanal und Rolle gelöscht und das Abo-Menü aktualisiert.",
            ephemeral: true);
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

        // Remove the auto-created category when it is left empty; one that still holds channels of
        // other feeds stays.
        try
        {
            if (Context.Guild is not IGuild guild)
            {
                return;
            }

            var categories = await guild.GetCategoriesAsync();
            var category = categories.FirstOrDefault(c => c.Name.Equals(RssDashboardModule.CategoryName, StringComparison.OrdinalIgnoreCase));
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
