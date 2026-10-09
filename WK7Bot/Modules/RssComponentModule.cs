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
            foreach (var value in selectedValues ?? Array.Empty<string>())
            {
                if (!ulong.TryParse(value, out var roleId))
                {
                    await FollowupAsync("❌ Die Auswahl konnte nicht verarbeitet werden. Bitte öffne das Abo-Menü mit `/rss dashboard` neu.", ephemeral: true);
                    return;
                }

                // Only roles the bot actually manages may be granted, so a stale dashboard whose
                // refresh failed cannot assign arbitrary server roles.
                if (allFeedRoleIds.Contains(roleId))
                {
                    selectedSet.Add(roleId);
                }
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
        catch (Exception ex)
        {
            // Component failures must reach the error pipeline, not just the clicking user.
            _logger.LogError(ex, "Error occurred while handling an RSS subscription select-menu interaction.");
            await FollowupAsync("❌ Beim Aktualisieren deiner Abos ist ein Fehler aufgetreten.", ephemeral: true);
        }
    }
}
