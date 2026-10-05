namespace WK7Bot.Core.Interfaces;

/// <summary>
/// Defines data access operations for persisting waste notification dispatch state so duplicate
/// messages are suppressed even after a bot restart.
/// </summary>
public interface IWasteDispatchRepository
{
    /// <summary>
    /// Determines whether a dispatch of the given kind was already sent to a guild on a specific date.
    /// </summary>
    /// <param name="kind">The dispatch kind (see <c>WasteDispatchKinds</c>).</param>
    /// <param name="guildId">The target Discord guild ID.</param>
    /// <param name="sentOn">The calendar date the message would be sent on.</param>
    /// <param name="cancellationToken">Cancellation token to observe.</param>
    /// <returns><see langword="true"/> when the message was already sent; otherwise <see langword="false"/>.</returns>
    Task<bool> HasSentAsync(string kind, ulong guildId, DateTime sentOn, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records that a dispatch of the given kind was sent to a guild on a specific date. Idempotent.
    /// </summary>
    /// <param name="kind">The dispatch kind (see <c>WasteDispatchKinds</c>).</param>
    /// <param name="guildId">The target Discord guild ID.</param>
    /// <param name="sentOn">The calendar date the message was sent on.</param>
    /// <param name="cancellationToken">Cancellation token to observe.</param>
    /// <returns>A task representing the asynchronous storage operation.</returns>
    Task MarkSentAsync(string kind, ulong guildId, DateTime sentOn, CancellationToken cancellationToken = default);
}
