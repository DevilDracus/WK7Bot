using System.Reflection;
using System.Runtime.CompilerServices;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;

namespace WK7Bot.Testing;

/// <summary>
/// Creates synthetic Discord interaction contexts for the HTTP command test API.
/// </summary>
/// <remarks>
/// Discord.Net socket types are hard to construct directly, so this factory uses reflection to populate
/// the members the interaction modules read. It is intentionally confined to this single place so the
/// fragile reflection logic is not duplicated across command runners and tests.
/// </remarks>
public static class SocketInteractionContextFactory
{
    /// <summary>
    /// Creates a synthetic <see cref="SocketInteractionContext"/> for the provided identity.
    /// </summary>
    /// <param name="services">The scoped service provider used to resolve the Discord socket client.</param>
    /// <param name="guildId">The guild ID the synthetic interaction belongs to.</param>
    /// <param name="userId">The user ID the command should run as.</param>
    /// <param name="username">The username the modules should display.</param>
    /// <returns>The constructed interaction context.</returns>
    public static SocketInteractionContext Create(
        IServiceProvider services,
        ulong guildId,
        ulong userId,
        string username)
    {
        ArgumentNullException.ThrowIfNull(services);

        var context = (SocketInteractionContext)RuntimeHelpers.GetUninitializedObject(typeof(SocketInteractionContext));

        var userField = FindField(context.GetType(), "<User>k__BackingField")
            ?? throw new InvalidOperationException("SocketInteractionContext user backing field not found.");
        userField.SetValue(context, CreateUser(username, userId));

        var client = services.GetRequiredService<DiscordSocketClient>();
        SetField(context, context.GetType(), "<Client>k__BackingField", client);
        SetField(context, context.GetType(), "_client", client);

        var guild = client.GetGuild(guildId);
        SetField(context, context.GetType(), "<Guild>k__BackingField", guild);
        SetField(context, context.GetType(), "_guild", guild);

        return context;
    }

    /// <summary>
    /// Creates a synthetic socket user carrying the provided identity.
    /// </summary>
    /// <param name="username">The username the modules should display.</param>
    /// <param name="id">The user ID, or <see langword="null"/> for an anonymous synthetic user.</param>
    /// <returns>The constructed socket user.</returns>
    public static SocketUser CreateUser(string username, ulong? id)
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
        if (property?.SetMethod is not null)
        {
            property.SetValue(target, value);
            return;
        }

        var field = FindField(type, $"<{name}>k__BackingField")
            ?? throw new InvalidOperationException($"{type.Name} {name} backing field not found.");
        field.SetValue(target, value);
    }

    private static FieldInfo? FindField(Type? type, string fieldName)
    {
        while (type is not null)
        {
            var field = type.GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field is not null)
            {
                return field;
            }

            type = type.BaseType;
        }

        return null;
    }

    private static void SetField(object target, Type type, string fieldName, object? value)
    {
        var field = FindField(type, fieldName);
        field?.SetValue(target, value);
    }
}
