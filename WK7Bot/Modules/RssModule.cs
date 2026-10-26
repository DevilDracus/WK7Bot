using System.Text;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;
using WK7Bot.Core.Entities;
using WK7Bot.Core.Interfaces;
using WK7Bot.Core.Utilities;
using WK7Bot.Services;
using WK7Bot.Services.Interfaces;

namespace WK7Bot.Modules;

/// <summary>
/// Provides slash commands for managing RSS news feeds: add, list, remove, dashboard, pause/resume,
/// preview, and OPML import/export.
/// </summary>
[Group("rss", "Befehle zur Verwaltung von RSS-Newsfeeds und Abonnements")]
public class RssModule : BotModuleBase
{
    /// <summary>
    /// Upper bound for feed names: the role becomes "RSS: {name}" (100-char role limit) and the dashboard's
    /// select-menu description "Benachrichtigungen f\u00fcr {name}" must fit into 100 characters.
    /// </summary>
    private const int MaxFeedNameLength = 70;

    /// <summary>Schema limit for <see cref="RssFeed.Url"/>; longer URLs are rejected before insert.</summary>
    private const int MaxFeedUrlLength = 500;

    internal const string CategoryName = "RSS Feeds";

    /// <summary>
    /// Discord renders at most 25 options (and 25 selected values) in one select menu.
    /// </summary>
    private const int MaxDashboardOptions = 25;

    /// <summary>Number of items shown by <c>/rss test</c>.</summary>
    private const int PreviewItemCount = 5;

    private readonly IRssRepository _repository;
    private readonly RssParserService _parser;
    private readonly IOpmlService _opmlService;
    private readonly ILogger<RssModule> _logger;

    /// <summary>
    /// Gets the logger used for error reporting.
    /// </summary>
    protected override ILogger Logger => _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="RssModule"/> class.
    /// </summary>
    /// <param name="repository">The repository used for RSS feed data persistence.</param>
    /// <param name="parser">The parser used by the feed preview command.</param>
    /// <param name="opmlService">The OPML import/export service.</param>
    /// <param name="logger">Logger instance used to surface command failures.</param>
    public RssModule(
        IRssRepository repository,
        RssParserService parser,
        IOpmlService opmlService,
        ILogger<RssModule> logger)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _parser = parser ?? throw new ArgumentNullException(nameof(parser));
        _opmlService = opmlService ?? throw new ArgumentNullException(nameof(opmlService));
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
        [Summary("url", "RSS-Feed-URL")] [MaxLength(MaxFeedUrlLength)] string url)
    {
        await DeferAsync(ephemeral: true).ConfigureAwait(false);

        var result = await CreateFeedAsync(name, url);
        if (result.Outcome != FeedCreationOutcome.Created)
        {
            await FollowupAsync($"\u274c {result.Message}", ephemeral: true).ConfigureAwait(false);
            return;
        }

        var dashboardUpdated = await TryRefreshDashboardAsync(Context, _repository);
        var dashboardNote = dashboardUpdated
            ? "Das Abo-Men\u00fc wurde aktualisiert."
            : "Es ist noch kein Abo-Men\u00fc vorhanden \u2014 erstelle eines mit `/rss dashboard`.";

        await FollowupAsync($"\u2705 {result.Message} {dashboardNote}", ephemeral: true).ConfigureAwait(false);
    }

    /// <summary>
    /// Pauses the polling of an existing feed without deleting its channel, role or baseline.
    /// </summary>
    /// <param name="name">The name of the feed to pause.</param>
    /// <returns>A task representing the command response operation.</returns>
    [SlashCommand("pause", "Pausiert die Aktualisierung eines Feeds")]
    [RequireUserPermission(GuildPermission.ManageChannels)]
    public Task PauseFeedAsync([Summary("name", "Name des Feeds")] [MaxLength(MaxFeedNameLength)] string name)
        => SetPausedAsync(name, paused: true);

    /// <summary>
    /// Resumes the polling of a paused feed.
    /// </summary>
    /// <param name="name">The name of the feed to resume.</param>
    /// <returns>A task representing the command response operation.</returns>
    [SlashCommand("resume", "Setzt die Aktualisierung eines Feeds fort")]
    [RequireUserPermission(GuildPermission.ManageChannels)]
    public Task ResumeFeedAsync([Summary("name", "Name des Feeds")] [MaxLength(MaxFeedNameLength)] string name)
        => SetPausedAsync(name, paused: false);

    /// <summary>
    /// Fetches a feed URL and shows a preview of its newest items without registering the feed.
    /// </summary>
    /// <param name="url">The feed URL to preview.</param>
    /// <returns>A task representing the command response operation.</returns>
    [SlashCommand("test", "Zeigt eine Vorschau der neuesten Eintr\u00e4ge einer Feed-URL")]
    public async Task TestFeedAsync([Summary("url", "RSS-Feed-URL")] [MaxLength(MaxFeedUrlLength)] string url)
    {
        await DeferAsync(ephemeral: true).ConfigureAwait(false);

        url = (url ?? string.Empty).Trim();
        if (!IsHttpUrl(url))
        {
            await FollowupAsync("\u274c Bitte gib eine g\u00fcltige HTTP(S)-Feed-URL an.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        var preview = await _parser.PreviewAsync(url, PreviewItemCount);
        if (preview is null)
        {
            await FollowupAsync(
                "\u274c Der Feed konnte nicht geladen oder geparst werden. Pr\u00fcfe die URL und ob es sich um RSS/Atom handelt.",
                ephemeral: true);
            return;
        }

        var embed = new EmbedBuilder()
            .WithTitle("\ud83d\udd0d Feed-Vorschau")
            .WithColor(Color.Teal)
            .WithTimestamp(DateTimeOffset.UtcNow)
            .WithFooter("WK7 Bot \u2022 RSS-Vorschau");

        var description = string.IsNullOrWhiteSpace(preview.Title)
            ? url
            : $"**{preview.Title}**\n{url}";
        embed.WithDescription(description);

        if (preview.Items.Count == 0)
        {
            embed.AddField("Eintr\u00e4ge", "Der Feed enth\u00e4lt keine Eintr\u00e4ge mit Datum.", inline: false);
        }
        else
        {
            foreach (var item in preview.Items)
            {
                var value = item.Published.HasValue
                    ? $"<t:{item.Published.Value.ToUnixTimeSeconds()}:f>"
                    : "kein Datum";

                if (!string.IsNullOrWhiteSpace(item.Link) && Uri.IsWellFormedUriString(item.Link, UriKind.Absolute))
                {
                    value += $" \u00b7 [\u00d6ffnen]({item.Link})";
                }

                embed.AddField(EmbedText.Truncate(item.Title, EmbedText.FieldNameLimit), value, inline: false);
            }
        }

        await FollowupAsync(embed: embed.Build(), ephemeral: true).ConfigureAwait(false);
    }

    /// <summary>
    /// Exports every registered feed as an OPML 2.0 file.
    /// </summary>
    /// <returns>A task representing the command response operation.</returns>
    [SlashCommand("export", "Exportiert alle Feeds als OPML-Datei")]
    [RequireUserPermission(GuildPermission.ManageChannels)]
    public async Task ExportOpmlAsync()
    {
        await DeferAsync(ephemeral: true).ConfigureAwait(false);

        try
        {
            var feeds = await _repository.GetAllFeedsAsync();
            if (feeds.Count == 0)
            {
                await FollowupAsync("\u274c Es sind keine Feeds zum Exportieren vorhanden.", ephemeral: true).ConfigureAwait(false);
                return;
            }

            var opml = _opmlService.BuildExport(feeds);
            await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(opml));
            await FollowupWithFileAsync(stream, "wk7-rss-feeds.opml", text: $"\ud83d\udce4 OPML-Export mit {feeds.Count} Feed(s).").ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Exporting the RSS feeds as OPML failed.");
            await FollowupAsync("\u274c Der OPML-Export ist fehlgeschlagen.", ephemeral: true).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Imports feeds from an attached OPML file. Existing names are skipped, and every new feed gets the
    /// same private channel and notification role as a feed added with <c>/rss add</c>.
    /// </summary>
    /// <param name="datei">The OPML file attachment.</param>
    /// <returns>A task representing the command response operation.</returns>
    [SlashCommand("import", "Importiert Feeds aus einer OPML-Datei")]
    [RequireUserPermission(GuildPermission.ManageChannels)]
    public async Task ImportOpmlAsync([Summary("datei", "OPML-Datei")] IAttachment datei)
    {
        await DeferAsync(ephemeral: true).ConfigureAwait(false);

        if (datei is null)
        {
            await FollowupAsync("\u274c Bitte h\u00e4nge eine OPML-Datei an.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        await ImportFromUrlCoreAsync(datei.Url).ConfigureAwait(false);
    }

    /// <summary>
    /// Imports feeds from an OPML document URL. Shared by the slash command (which passes the
    /// attachment URL) and the HTTP test API (which passes a plain URL).
    /// </summary>
    /// <param name="url">The OPML document URL.</param>
    /// <returns>A task representing the import operation.</returns>
    internal async Task ImportFromUrlAsync(string url)
    {
        await DeferAsync(ephemeral: true).ConfigureAwait(false);
        await ImportFromUrlCoreAsync(url).ConfigureAwait(false);
    }

    /// <summary>Runs the OPML import after the interaction has been deferred.</summary>
    /// <param name="url">The OPML document URL.</param>
    /// <returns>A task representing the import operation.</returns>
    private async Task ImportFromUrlCoreAsync(string url)
    {
        var entries = await _opmlService.FetchAsync(url);
        if (entries.Count == 0)
        {
            await FollowupAsync(
                "\u274c In der OPML-Datei wurden keine Feeds gefunden (oder die Datei konnte nicht geladen werden).",
                ephemeral: true);
            return;
        }

        var created = new List<string>();
        var skipped = new List<string>();
        var failed = new List<string>();

        foreach (var entry in entries)
        {
            var result = await CreateFeedAsync(entry.Title, entry.Url);
            switch (result.Outcome)
            {
                case FeedCreationOutcome.Created:
                    created.Add(entry.Title);
                    break;
                case FeedCreationOutcome.AlreadyExists:
                    skipped.Add(entry.Title);
                    break;
                default:
                    failed.Add(entry.Title);
                    break;
            }
        }

        if (created.Count > 0)
        {
            await TryRefreshDashboardAsync(Context, _repository).ConfigureAwait(false);
        }

        var summary = new StringBuilder();
        summary.AppendLine($"\ud83d\udce5 OPML-Import abgeschlossen: **{created.Count}** neu, **{skipped.Count}** \u00fcbersprungen, **{failed.Count}** fehlgeschlagen.");
        if (created.Count > 0)
        {
            summary.AppendLine($"Neu: {string.Join(", ", created.Take(10))}");
        }

        if (skipped.Count > 0)
        {
            summary.AppendLine($"Bereits vorhanden: {string.Join(", ", skipped.Take(10))}");
        }

        if (failed.Count > 0)
        {
            summary.AppendLine($"Fehlgeschlagen: {string.Join(", ", failed.Take(10))}");
        }

        await FollowupAsync(summary.ToString().TrimEnd(), ephemeral: true).ConfigureAwait(false);
    }

    /// <summary>
    /// Lists all registered RSS feeds with their names, URLs, target channels, polling interval and state.
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
                await RespondAsync("Noch keine RSS-Feeds registriert. F\u00fcge einen mit `/rss add` hinzu.", ephemeral: true).ConfigureAwait(false);
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
                var state = feed.IsPaused ? "\u23f8\ufe0f pausiert" : "\u25b6\ufe0f aktiv";
                embed.AddField(
                    feed.Name,
                    $"{feed.Url}\nKanal: {channelMention}\nIntervall: {feed.RefreshIntervalMinutes} min \u00b7 {state}",
                    inline: false);
            }

            await RespondAsync(embed: embed.Build(), ephemeral: true).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Listing RSS feeds failed.");
            await RespondAsync("\u274c Feeds konnten nicht geladen werden.", ephemeral: true).ConfigureAwait(false);
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
        await DeferAsync(ephemeral: true).ConfigureAwait(false);

        name = name.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            await FollowupAsync("\u274c Bitte gib den Namen des zu entfernenden Feeds an.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        var feed = await _repository.GetFeedByNameAsync(name);
        if (feed == null)
        {
            await FollowupAsync($"\u274c Kein Feed mit dem Namen '{name}' gefunden.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        try
        {
            await _repository.DeleteFeedAsync(feed.Id).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Removing an RSS feed from the database failed.");
            await FollowupAsync($"\u274c Das Entfernen von **{feed.Name}** aus der Datenbank ist fehlgeschlagen; es wurde nichts ge\u00e4ndert. Bitte versuche es erneut.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        var cleanupProblem = false;
        var guild = Context.Guild;

        if (guild.GetChannel(feed.ChannelId) is IGuildChannel channel)
        {
            try
            {
                await channel.DeleteAsync().ConfigureAwait(false);
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
                await role.DeleteAsync().ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                cleanupProblem = true;
                _logger.LogError(ex, "RSS feed {Name} was removed, but its role {RoleId} could not be deleted.", feed.Name, feed.RoleId);
            }
        }

        try
        {
            await TryRefreshDashboardAsync(Context, _repository).ConfigureAwait(false);
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
                await RespondAsync("\u274c Es sind noch keine RSS-Feeds zum Abonnieren vorhanden.", ephemeral: true).ConfigureAwait(false);
                return;
            }

            var (embed, component) = BuildDashboardMessage(feeds);

            await RespondAsync(embed: embed, components: component).ConfigureAwait(false);
            responseSent = true;

            var responseMessage = await GetOriginalResponseAsync();
            await _repository.SaveDashboardLocationAsync(Context.Channel.Id, responseMessage.Id).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Posting the RSS subscription dashboard failed.");

            var message = responseSent
                ? "\u26a0\ufe0f Das Abo-Men\u00fc wurde gepostet, aber seine Position konnte nicht gespeichert werden \u2014 automatische Aktualisierungen funktionieren erst, nachdem es erneut gepostet wurde."
                : "\u274c Das Abo-Men\u00fc konnte nicht erstellt werden. Bitte versuche es sp\u00e4ter erneut.";

            if (responseSent)
            {
                await FollowupAsync(message, ephemeral: true).ConfigureAwait(false);
            }
            else
            {
                await RespondAsync(message, ephemeral: true).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Creates a feed with its private channel and notification role. Shared by <c>/rss add</c> and
    /// <c>/rss import</c> so both paths provision identical structures and roll back partial creations.
    /// </summary>
    /// <param name="name">The requested feed name.</param>
    /// <param name="url">The requested feed URL.</param>
    /// <returns>The creation outcome and a user-facing message.</returns>
    private async Task<FeedCreationResult> CreateFeedAsync(string name, string url)
    {
        name = (name ?? string.Empty).Trim();
        url = (url ?? string.Empty).Trim();

        if (name.Length == 0 || name.Length > MaxFeedNameLength)
        {
            return new FeedCreationResult(FeedCreationOutcome.Invalid, $"Der Name muss zwischen 1 und {MaxFeedNameLength} Zeichen lang sein.");
        }

        if (!IsHttpUrl(url) || url.Length > MaxFeedUrlLength)
        {
            return new FeedCreationResult(FeedCreationOutcome.Invalid, $"'{url}' ist keine g\u00fcltige HTTP(S)-Feed-URL.");
        }

        var existing = await _repository.GetFeedByNameAsync(name);
        if (existing != null)
        {
            return new FeedCreationResult(
                FeedCreationOutcome.AlreadyExists,
                $"Ein Feed mit dem Namen '{name}' existiert bereits. W\u00e4hle einen anderen Namen oder entferne den vorhandenen Feed zuerst.");
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

            await _repository.AddFeedAsync(feedEntity).ConfigureAwait(false);
            feedSaved = true;

            return new FeedCreationResult(
                FeedCreationOutcome.Created,
                $"Privater Nur-Lese-Kanal <#{createdChannel.Id}> und Benachrichtigungsrolle <@&{createdRole.Id}> f\u00fcr den RSS-Feed **{name}** erstellt.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Adding an RSS feed failed after the interaction was deferred.");

            if (!feedSaved)
            {
                await TryCleanupPartialCreationAsync(createdChannel, createdRole).ConfigureAwait(false);
                return new FeedCreationResult(
                    FeedCreationOutcome.Failed,
                    $"Das Erstellen des Feeds **{name}** ist fehlgeschlagen; teilweise erstellte Kan\u00e4le, Rollen und eine leere Kategorie wurden aufger\u00e4umt.");
            }

            return new FeedCreationResult(
                FeedCreationOutcome.Created,
                $"Der Feed **{name}** wurde erstellt, aber das Aufr\u00e4umen ist fehlgeschlagen \u2014 bitte pr\u00fcfe den Server.");
        }
    }

    /// <summary>
    /// Pauses or resumes one feed and persists the new state.
    /// </summary>
    /// <param name="name">The feed name.</param>
    /// <param name="paused">The target state.</param>
    /// <returns>A task representing the command response operation.</returns>
    private async Task SetPausedAsync(string name, bool paused)
    {
        await DeferAsync(ephemeral: true).ConfigureAwait(false);

        name = (name ?? string.Empty).Trim();
        if (name.Length == 0)
        {
            await FollowupAsync("\u274c Bitte gib den Namen des Feeds an.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        var feed = await _repository.GetFeedByNameAsync(name);
        if (feed == null)
        {
            await FollowupAsync($"\u274c Kein Feed mit dem Namen '{name}' gefunden.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        if (feed.IsPaused == paused)
        {
            await FollowupAsync(
                $"\u2139\ufe0f Der Feed **{feed.Name}** ist bereits {(paused ? "pausiert" : "aktiv")}.",
                ephemeral: true);
            return;
        }

        try
        {
            feed.IsPaused = paused;
            await _repository.UpdateFeedAsync(feed).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Updating the paused state of RSS feed {Name} failed.", name);
            await FollowupAsync($"\u274c Der Status von **{feed.Name}** konnte nicht gespeichert werden.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        await FollowupAsync(
            $"{(paused ? "\u23f8\ufe0f" : "\u25b6\ufe0f")} Der Feed **{feed.Name}** ist jetzt {(paused ? "pausiert" : "wieder aktiv")}.",
            ephemeral: true);
    }

    /// <summary>Validates that a string is an absolute HTTP(S) URL.</summary>
    /// <param name="url">The candidate URL.</param>
    /// <returns><see langword="true"/> when the URL is usable for a feed request.</returns>
    private static bool IsHttpUrl(string? url)
        => !string.IsNullOrWhiteSpace(url)
           && Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri)
           && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

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
    private static (Embed Embed, MessageComponent Component) BuildDashboardMessage(IReadOnlyList<RssFeed> feeds)
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
                await channel.DeleteAsync().ConfigureAwait(false);
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
                await role.DeleteAsync().ConfigureAwait(false);
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
                await category.DeleteAsync().ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Rollback of a failed RSS feed creation could not delete the empty category.");
        }
    }

    /// <summary>Outcome of a feed creation attempt.</summary>
    private enum FeedCreationOutcome
    {
        /// <summary>The feed, channel and role were created.</summary>
        Created,

        /// <summary>A feed with the same name already exists.</summary>
        AlreadyExists,

        /// <summary>The supplied name or URL failed validation.</summary>
        Invalid,

        /// <summary>Provisioning failed and any partial creations were rolled back.</summary>
        Failed
    }

    /// <summary>Result of a feed creation attempt.</summary>
    /// <param name="Outcome">The outcome.</param>
    /// <param name="Message">A user-facing message describing the outcome.</param>
    private sealed record FeedCreationResult(FeedCreationOutcome Outcome, string Message);
}
