using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;

namespace WK7Bot.Modules;

/// <summary>
/// Common base class for Discord interaction modules. Provides a shared logger abstraction and a
/// single place for future cross-cutting module behaviour (error formatting, rate-limit helpers, etc.).
/// </summary>
public abstract class BotModuleBase : InteractionModuleBase<SocketInteractionContext>
{
    /// <summary>
    /// Gets the logger used for this module.
    /// </summary>
    protected abstract ILogger Logger { get; }
}
