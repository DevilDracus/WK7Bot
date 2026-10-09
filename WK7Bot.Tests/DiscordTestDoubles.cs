using System;
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

        var user = (SocketUser)RuntimeHelpers.GetUninitializedObject(userType);
        SetPropertyOrField(user, userType, "Username", username);
        if (id.HasValue)
        {
            SetPropertyOrField(user, userType, "Id", id.Value);
        }

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
    public static FieldInfo? FindField(Type type, string fieldName)
    {
        while (type != null)
        {
            var field = type.GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (field != null)
            {
                return field;
            }

            type = type.BaseType!;
        }

        return null;
    }
}
