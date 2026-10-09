namespace WK7Bot.Core.Utilities;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using Discord.Net;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;

/// <summary>
/// Shared find-or-create resolution for feature channels so every feature looks its channel up the
/// same way: a case-insensitive name match in the guild wins, otherwise a new channel is created
/// with the topic and overwrites supplied by the caller. The configure callback only runs when the
/// channel is actually created.
/// </summary>
public static class ChannelResolver
{
    /// <summary>
    /// Returns the guild's text channel with the given name, creating it when missing.
    /// </summary>
    /// <param name="guild">The guild that owns the channel.</param>
    /// <param name="channelName">The channel name to look up (case-insensitive).</param>
    /// <param name="configure">Applied only on creation (topic, permission overwrites, category, ...).</param>
    /// <returns>The existing or newly created text channel.</returns>
    private static async Task<ITextChannel> GetOrCreateChannelAsync(
        SocketGuild guild,
        string channelName,
        Action<TextChannelProperties> configure)
    {
        ArgumentNullException.ThrowIfNull(guild);
        ArgumentNullException.ThrowIfNull(configure);
        ArgumentNullException.ThrowIfNull(channelName);

        var existingChannel = guild.TextChannels.FirstOrDefault(c => c.Name.Equals(channelName, StringComparison.OrdinalIgnoreCase));
        if (existingChannel != null)
        {
            return existingChannel;
        }

        return await guild.CreateTextChannelAsync(channelName, configure);
    }

    /// <summary>
    /// Applies the standard read-only permission setup to channel properties: @everyone may read the
    /// channel and its history but not send messages, while the bot may send messages and embed links.
    /// </summary>
    /// <param name="properties">The channel properties to configure.</param>
    /// <param name="guild">The guild the channel belongs to (source of the @everyone role).</param>
    /// <param name="botUserId">The bot's own user ID.</param>
    /// <param name="allowReactions">Whether @everyone may additionally add reactions.</param>
    private static void ApplyDefaultChannelPermissions(
        TextChannelProperties properties,
        SocketGuild guild,
        ulong botUserId,
        bool allowReactions)
    {
        ArgumentNullException.ThrowIfNull(properties);
        ArgumentNullException.ThrowIfNull(guild);

        properties.PermissionOverwrites = new List<Overwrite>
        {
            new(guild.EveryoneRole.Id, PermissionTarget.Role, new OverwritePermissions(
                viewChannel: PermValue.Allow,
                readMessageHistory: PermValue.Allow,
                sendMessages: PermValue.Deny,
                addReactions: allowReactions ? PermValue.Allow : PermValue.Deny
            )),
            new(botUserId, PermissionTarget.User, new OverwritePermissions(
                viewChannel: PermValue.Allow,
                readMessageHistory: PermValue.Allow,
                sendMessages: PermValue.Allow,
                embedLinks: PermValue.Allow
            ))
        };
    }

    /// <summary>
    /// Resolves a feature channel inside a guild, creating it when missing, and converts a Discord
    /// permission failure into a warning so a missing Manage-Channels permission cannot escalate into
    /// the error-notification pipeline on every poll or dispatch.
    /// </summary>
    /// <param name="guild">The guild that should own the channel.</param>
    /// <param name="botUserId">The bot's own user ID (granted read/write on creation).</param>
    /// <param name="channelName">The feature channel name to look up or create.</param>
    /// <param name="topic">Topic applied when the channel is created.</param>
    /// <param name="logger">Logger that receives the warning when creation is forbidden.</param>
    /// <param name="allowReactions">Whether @everyone may additionally add reactions.</param>
    /// <returns>The existing or newly created channel, or <see langword="null"/> when creation failed.</returns>
    public static async Task<ITextChannel?> TryGetOrCreateFeatureChannelAsync(
        SocketGuild guild,
        ulong botUserId,
        string channelName,
        string topic,
        ILogger logger,
        bool allowReactions = false)
    {
        ArgumentNullException.ThrowIfNull(guild);
        ArgumentNullException.ThrowIfNull(logger);

        try
        {
            return await GetOrCreateChannelAsync(guild, channelName, properties =>
            {
                properties.Topic = topic;
                ApplyDefaultChannelPermissions(properties, guild, botUserId, allowReactions);
            });
        }
        catch (HttpException ex)
        {
            logger.LogWarning(ex, "Could not find or create the #{Channel} channel.", channelName);
            return null;
        }
    }
}
