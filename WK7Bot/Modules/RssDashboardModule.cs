using Discord;
using Discord.Interactions;
using Microsoft.Extensions.Logging;
using WK7Bot.Core.Entities;
using WK7Bot.Core.Interfaces;

namespace WK7Bot.Modules;

/// <summary>
/// Provides the slash commands for the RSS subscription dashboard (select menu).
/// Feed lifecycle commands (add / list / remove) live in <see cref="RssFeedManagementModule"/>.
/// </summary>
[Group("rss", "Befehle zur Verwaltung von RSS-Newsfeeds und Abonnements")]
public class RssDashboardModule : InteractionModuleBase<SocketInteractionContext>
{
    internal const string CategoryName = "RSS Feeds";

    /// <summary>
    /// Discord renders at most 25 options (and 25 selected values) in one select menu.
    /// </summary>
    private const int MaxDashboardOptions = 25;

    private readonly IRssRepository _repository;
    private readonly ILogger<RssDashboardModule> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="RssDashboardModule"/> class.
    /// </summary>
    /// <param name="repository">The repository used for RSS feed data persistence.</param>
    /// <param name="logger">Logger instance used to surface dashboard failures.</param>
    public RssDashboardModule(IRssRepository repository, ILogger<RssDashboardModule> logger)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Posts or updates the interactive subscription dashboard select menu in the current channel and stores its location.
    /// </summary>
    /// <returns>A task representing the command response operation.</returns>
    [SlashCommand("dashboard", "Postet das interaktive RSS-Abo-Menü")]
    [RequireUserPermission(GuildPermission.ManageRoles)]
    public async Task PostDashboardAsync()
    {
        var responseSent = false;
        try
        {
            var feeds = await _repository.GetAllFeedsAsync();

            if (!feeds.Any())
            {
                await RespondAsync("❌ Es sind noch keine RSS-Feeds zum Abonnieren vorhanden.", ephemeral: true);
                return;
            }

            var (embed, component) = BuildDashboardMessage(feeds);

            // The dashboard must NOT be ephemeral: RefreshDashboardMessageAsync fetches it by channel/message ID,
            // which is impossible for ephemeral messages, so the stored location would be dead on arrival.
            await RespondAsync(embed: embed, components: component);
            responseSent = true;

            var responseMessage = await GetOriginalResponseAsync();
            await _repository.SaveDashboardLocationAsync(Context.Channel.Id, responseMessage.Id);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Without this guard a failing database read leaves the interaction unanswered and only
            // surfaces as an error embed.
            _logger.LogError(ex, "Posting the RSS subscription dashboard failed.");

            var message = responseSent
                ? "⚠️ Das Abo-Menü wurde gepostet, aber seine Position konnte nicht gespeichert werden — automatische Aktualisierungen funktionieren erst, nachdem es erneut gepostet wurde."
                : "❌ Das Abo-Menü konnte nicht erstellt werden. Bitte versuche es später erneut.";

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
    /// Used by the lifecycle commands in <see cref="RssFeedManagementModule"/>, which must not own
    /// the dashboard-rendering and -storage logic.
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
            // The stored dashboard message was deleted; there is nothing left to refresh.
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
        // Discord renders at most 25 options per select menu. Extra feeds stay registered but are
        // reported in the description so they are not silently invisible.
        var shownFeeds = feeds.Take(MaxDashboardOptions).ToList();
        var hiddenCount = feeds.Count - shownFeeds.Count;

        var menuBuilder = new SelectMenuBuilder()
            .WithCustomId("rss-subscription-select")
            .WithPlaceholder("Wähle die RSS-Feeds aus, die du abonnieren möchtest...")
            .WithMinValues(0)
            .WithMaxValues(shownFeeds.Count);

        foreach (var feed in shownFeeds)
        {
            menuBuilder.AddOption(
                label: feed.Name,
                value: feed.RoleId.ToString(),
                description: $"Benachrichtigungen für {feed.Name}");
        }

        var component = new ComponentBuilder()
            .WithSelectMenu(menuBuilder)
            .Build();

        var description = "Wähle im Menü unten aus, welche lokalen Nachrichten und Polizeimeldungen du abonnieren möchtest. Die Auswahl wirkt sich sofort auf deine Benachrichtigungsrollen aus.";
        if (hiddenCount > 0)
        {
            description += $"\n\n⚠️ {hiddenCount} weitere Feeds sind eingerichtet, zeigt Discord in diesem Menü aus Platzgründen nicht an.";
        }

        var embed = new EmbedBuilder()
            .WithTitle("RSS-Feed-Abos")
            .WithDescription(description)
            .WithColor(Color.Blue)
            .Build();

        return (embed, component);
    }
}
