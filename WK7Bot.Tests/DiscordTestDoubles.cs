using System;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;

namespace WK7Bot.Tests;
/// <summary>
/// Shared doubles for the interaction-module tests: Discord.Net's socket types cannot be constructed
/// directly (no public constructor, internal setters), so the tests build uninitialised instances and
/// populate the members the modules read through these helpers.
/// </summary>
internal static class DiscordTestDoubles
{
    /// <summary>
    /// Creates an uninitialised interaction context. Centralised so the fragile construction lives
    /// in a single place.
    /// </summary>
    public static SocketInteractionContext CreateContext() =>
        (SocketInteractionContext)RuntimeHelpers.GetUninitializedObject(typeof(SocketInteractionContext));

    /// <summary>
    /// Creates a real (but inert) socket client. Prefers the public constructor over
    /// <see cref="RuntimeHelpers.GetUninitializedObject"/> so we no longer depend on the type's
    /// internal layout.
    /// </summary>
    public static DiscordSocketClient CreateClient() => new();

    /// <summary>
    /// Creates a real (but inert) socket guild with the given identity.
    /// </summary>
    /// <param name="id">The guild ID.</param>
    /// <returns>The populated guild instance.</returns>
    public static SocketGuild CreateGuild(ulong id)
    {
        var guildType = typeof(SocketGuild);
        var ctor = guildType.GetConstructor(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null,
            new[] { typeof(DiscordSocketClient), typeof(ulong) },
            null)
            ?? throw new InvalidOperationException("SocketGuild constructor not found.");

        return (SocketGuild)ctor.Invoke(new object?[] { CreateClient(), id });
    }

    /// <summary>
    /// Attaches a socket guild with the given identity to an uninitialised interaction context.
    /// </summary>
    /// <param name="context">The uninitialised context.</param>
    /// <param name="guildId">The guild ID the modules see.</param>
    public static void SetGuild(SocketInteractionContext context, ulong guildId)
    {
        var guild = CreateGuild(guildId);
        SetPropertyOrField(context, context.GetType(), "Guild", guild);
    }

    /// <summary>
    /// Attaches a socket user with the given identity to an uninitialised interaction context.
    /// </summary>
    /// <param name="context">The uninitialised context.</param>
    /// <param name="username">The username the modules see.</param>
    /// <param name="id">The user ID; omit when the module under test does not read it.</param>
    public static void SetUser(SocketInteractionContext context, string username, ulong? id = null)
    {
        var userField = FindField(context.GetType(), "<User>k__BackingField")
            ?? throw new InvalidOperationException("SocketInteractionContext user backing field not found.");
        userField.SetValue(context, CreateUser(username, id));
    }

    /// <summary>
    /// Creates a socket user carrying the given identity.
    /// </summary>
    /// <param name="username">The username the modules see.</param>
    /// <param name="id">The user ID; omit when the module under test does not read it.</param>
    /// <returns>The populated user instance.</returns>
    public static SocketUser CreateUser(string username, ulong? id = null)
    {
        var userType = typeof(SocketUser).Assembly.GetType("Discord.WebSocket.SocketGlobalUser")
            ?? throw new InvalidOperationException("Discord.WebSocket.SocketGlobalUser type not found.");

        // Prefer the type's own constructor so the identity (Id) is initialised the way Discord.Net
        // intends, instead of poking the compiler-generated backing field. Fall back to the legacy
        // uninitialised-object path only if the constructor shape changes.
        SocketUser user;
        try
        {
            var ctor = userType.GetConstructor(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                new[] { typeof(DiscordSocketClient), typeof(ulong) },
                null)
                ?? throw new InvalidOperationException("SocketGlobalUser constructor not found.");

            user = (SocketUser)ctor.Invoke(new object?[] { CreateClient(), id ?? 0UL });
        }
        catch
        {
            user = (SocketUser)RuntimeHelpers.GetUninitializedObject(userType);
            if (id.HasValue)
            {
                SetPropertyOrField(user, userType, "Id", id.Value);
            }
        }

        SetPropertyOrField(user, userType, "Username", username);
        return user;
    }

    /// <summary>
    /// Assigns a value to a property or its compiler-generated backing field.
    /// </summary>
    /// <param name="target">The instance to populate.</param>
    /// <param name="type">The type declaring the member.</param>
    /// <param name="name">The property name.</param>
    /// <param name="value">The value to assign.</param>
    public static void SetPropertyOrField(object target, Type type, string name, object value)
    {
        var property = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (property?.SetMethod != null)
        {
            property.SetValue(target, value);
            return;
        }

        var field = FindField(type, $"<{name}>k__BackingField")
            ?? throw new InvalidOperationException($"{type.Name} {name} backing field not found.");
        field.SetValue(target, value);
    }

    /// <summary>
    /// Finds an instance field on a type or any of its base types.
    /// </summary>
    /// <param name="type">The type to search.</param>
    /// <param name="fieldName">The field name to look for.</param>
    /// <returns>The field, or null when no type in the hierarchy declares it.</returns>
    public static FieldInfo? FindField(Type? type, string fieldName)
    {
        while (type is not null)
        {
            var field = type.GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (field != null)
            {
                return field;
            }

            // Fall back to a looser match for compiler-generated backing fields whose exact
            // name may vary between compiler versions (e.g. "<User>k__BackingField").
            var needle = fieldName.Trim('<', '>');
            if (needle.EndsWith("k__BackingField", StringComparison.Ordinal))
            {
                needle = needle[..^"k__BackingField".Length];
            }

            field = type.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                .FirstOrDefault(f => f.Name.Contains(needle, StringComparison.Ordinal)
                    && f.Name.Contains("BackingField", StringComparison.Ordinal));
            if (field != null)
            {
                return field;
            }

            // Last resort: an exact simple field name such as "_client".
            field = type.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                .FirstOrDefault(f => f.Name.Equals(fieldName, StringComparison.Ordinal));
            if (field != null)
            {
                return field;
            }

            type = type.BaseType;
        }

        return null;
    }
}