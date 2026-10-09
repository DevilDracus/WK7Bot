namespace WK7Bot.Core.Utilities;

using System;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using Discord.WebSocket;

/// <summary>
/// Shared Discord client/guild lookups used across the command modules and the background services.
/// </summary>
public static class DiscordClientExtensions
{
    /// <summary>
    /// Finds a text channel by name, ignoring case so a renamed or differently-cased channel is still found.
    /// </summary>
    /// <param name="guild">The guild to search; <see langword="null"/> yields <see langword="null"/>.</param>
    /// <param name="channelName">The channel name to look up.</param>
    /// <returns>The matching text channel, or <see langword="null"/>.</returns>
    public static ITextChannel? FindTextChannel(this SocketGuild? guild, string channelName)
        => guild?.TextChannels.FirstOrDefault(channel =>
            string.Equals(channel.Name, channelName, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Runs an action for every non-bot user of every guild the client is in.
    /// </summary>
    /// <param name="client">The Discord socket client.</param>
    /// <param name="filter">Predicate deciding which users the action runs for.</param>
    /// <param name="action">The action to run per matching user.</param>
    /// <returns>A task that completes once every matching user was processed.</returns>
    public static async Task ForEachGuildUserAsync(
        this DiscordSocketClient client,
        Func<SocketGuildUser, bool> filter,
        Func<SocketGuildUser, Task> action)
    {
        foreach (var guild in client.Guilds)
        {
            foreach (var user in guild.Users)
            {
                if (!user.IsBot && filter(user))
                {
                    await action(user);
                }
            }
        }
    }
}
