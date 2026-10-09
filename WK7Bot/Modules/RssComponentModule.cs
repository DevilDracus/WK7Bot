using System.Net;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;
using WK7Bot.Core.Interfaces;

namespace WK7Bot.Modules;

/// <summary>
/// Handles UI component interactions such as select menus and buttons for RSS feed subscriptions.
/// </summary>
public class RssComponentModule : InteractionModuleBase<SocketInteractionContext>
{
    private readonly IRssRepository _repository;
    private readonly ILogger<RssComponentModule> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="RssComponentModule"/> class.
    /// </summary>
    /// <param name="repository">The repository used for RSS feed data persistence.</param>
    /// <param name="logger">The logger instance for diagnostics.</param>
    public RssComponentModule(IRssRepository repository, ILogger<RssComponentModule> logger)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Handles user selections from the RSS subscription multi-select menu component.
    /// Extracts selected role identifiers, catches hierarchy and permission exceptions, and updates user server roles in batch operations.
    /// </summary>
    /// <param name="selectedValues">An array of string values selected by the user from the multi-select menu.</param>
    /// <returns>A task representing the asynchronous interaction handling operation.</returns>
    [ComponentInteraction("rss-subscription-select")]
    public async Task HandleSubscriptionSelectionAsync(string[] selectedValues)
    {
        await DeferAsync(ephemeral: true);

        try
        {
            if (Context.User is not SocketGuildUser guildUser)
            {
                await FollowupAsync("❌ Diese Aktion ist nur innerhalb eines Servers möglich.", ephemeral: true);
                return;
            }

            var feeds = await _repository.GetAllFeedsAsync();
            var allFeedRoleIds = feeds.Select(f => f.RoleId).ToHashSet();

            var selectedSet = new HashSet<ulong>();
            var staleMenu = false;

            foreach (var value in selectedValues ?? Array.Empty<string>())
            {
                var parsed = ulong.TryParse(value, out var roleId);
                if (!parsed || !allFeedRoleIds.Contains(roleId))
                {
                    // A non-numeric value or a role that is not a (current) feed role means the menu
                    // is out of date.
                    staleMenu = true;
                    continue;
                }

                selectedSet.Add(roleId);
            }

            if (staleMenu)
            {
                // Applying a stale menu would silently drop existing subscriptions, so keep the
                // current roles untouched and ask for a fresh menu instead.
                await FollowupAsync("⚠️ Dieses Abo-Menü ist veraltet. Bitte öffne ein neues mit `/rss dashboard`.", ephemeral: true);
                return;
            }

            var rolesToAdd = selectedSet
                .Where(roleId => !guildUser.Roles.Any(r => r.Id == roleId))
                .ToList();

            var rolesToRemove = allFeedRoleIds
                .Where(roleId => !selectedSet.Contains(roleId) && guildUser.Roles.Any(r => r.Id == roleId))
                .ToList();

            if (rolesToAdd.Count > 0)
            {
                await guildUser.AddRolesAsync(rolesToAdd);
            }

            if (rolesToRemove.Count > 0)
            {
                await guildUser.RemoveRolesAsync(rolesToRemove);
            }

            await FollowupAsync("✅ Deine RSS-Feed-Abos wurden aktualisiert!", ephemeral: true);
        }
        catch (Discord.Net.HttpException ex) when (ex.HttpCode == HttpStatusCode.Forbidden)
        {
            await FollowupAsync("❌ Die Rollen konnten nicht aktualisiert werden: Dem Bot fehlt die Berechtigung oder seine Rolle steht in der Hierarchie unter den RSS-Rollen. Bitte verschiebe die Bot-Rolle über die RSS-Rollen (Servereinstellungen > Rollen).", ephemeral: true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Component failures must reach the error pipeline, not just the clicking user.
            _logger.LogError(ex, "Error occurred while handling an RSS subscription select-menu interaction.");
            await FollowupAsync("❌ Beim Aktualisieren deiner Abos ist ein Fehler aufgetreten.", ephemeral: true);
        }
    }
}
