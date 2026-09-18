namespace WK7Bot.Modules;

using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using System.Net;
using WK7Bot.Core.Interfaces;

/// <summary>
/// Handles UI component interactions such as select menus and buttons for RSS feed subscriptions.
/// </summary>
public class RssComponentModule : InteractionModuleBase<SocketInteractionContext>
{
    private readonly IRssRepository _repository;

    /// <summary>
    /// Initializes a new instance of the <see cref="RssComponentModule"/> class.
    /// </summary>
    /// <param name="repository">The repository used for RSS feed data persistence.</param>
    public RssComponentModule(IRssRepository repository)
    {
        _repository = repository;
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
                await FollowupAsync("This action can only be performed within a server.", ephemeral: true);
                return;
            }

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

            if (rolesToAdd.Count > 0)
            {
                await guildUser.AddRolesAsync(rolesToAdd);
            }

            if (rolesToRemove.Count > 0)
            {
                await guildUser.RemoveRolesAsync(rolesToRemove);
            }

            await FollowupAsync("Your RSS feed subscriptions have been successfully updated!", ephemeral: true);
        }
        catch (Discord.Net.HttpException ex) when (ex.HttpCode == HttpStatusCode.Forbidden)
        {
            await FollowupAsync("Failed to update roles: The bot lacks permission or its role is positioned lower in the server hierarchy than the RSS roles. Please move the bot's role above the RSS roles in Server Settings > Roles.", ephemeral: true);
        }
        catch (Exception ex)
        {
            await FollowupAsync($"An error occurred while updating your subscriptions: {ex.Message}", ephemeral: true);
        }
    }
}