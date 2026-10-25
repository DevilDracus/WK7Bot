using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;

namespace WK7Bot.Modules;

/// <summary>
/// Common base class for Discord interaction modules that provides a shared error-reporting helper.
/// </summary>
public abstract class BotModuleBase : InteractionModuleBase<SocketInteractionContext>
{
    /// <summary>
    /// Gets the logger used for error reporting.
    /// </summary>
    protected abstract ILogger Logger { get; }

    /// <summary>
    /// Reports an unexpected exception to the logger and sends an ephemeral error message to the user.
    /// </summary>
    /// <param name="ex">The exception that was thrown.</param>
    /// <param name="userMessage">The message to show to the user.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    protected async Task ReportErrorAsync(Exception ex, string userMessage)
    {
        Logger.LogError(ex, "Error occurred while executing a slash command.");
        await FollowupAsync(userMessage, ephemeral: true).ConfigureAwait(false);
    }

    /// <summary>
    /// Determines whether the given exception represents a transient or cancellation error that should not be reported as a hard failure.
    /// </summary>
    /// <param name="ex">The exception to check.</param>
    /// <returns><see langword="true"/> when the exception is a cancellation or transient error.</returns>
    protected static bool IsTransientOrCancellation(Exception ex) =>
        ex is not OperationCanceledException || ex is TaskCanceledException;
}
