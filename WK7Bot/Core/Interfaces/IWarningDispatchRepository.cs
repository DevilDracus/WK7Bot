namespace WK7Bot.Core.Interfaces;

using WK7Bot.Models;

/// <summary>
/// Defines data access operations for persisting which DWD warnings were already posted to a guild,
/// providing restart-safe duplicate suppression for the warning feature.
/// </summary>
public interface IWarningDispatchRepository
{
    /// <summary>
    /// Determines whether the warning with the given identifier was already posted to a guild.
    /// </summary>
    /// <param name="warningId">The stable CAP warning identifier.</param>
    /// <param name="guildId">The target Discord guild ID.</param>
    /// <param name="cancellationToken">Cancellation token to observe.</param>
    /// <returns><see langword="true"/> when the warning was already posted; otherwise <see langword="false"/>.</returns>
    Task<bool> HasSentAsync(string warningId, ulong guildId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records that the warning with the given identifier was posted to a guild. Idempotent.
    /// </summary>
    /// <param name="warningId">The stable CAP warning identifier.</param>
    /// <param name="guildId">The target Discord guild ID.</param>
    /// <param name="sentAt">The local time the message was sent.</param>
    /// <param name="cancellationToken">Cancellation token to observe.</param>
    /// <returns>A task representing the asynchronous storage operation.</returns>
    Task MarkSentAsync(string warningId, ulong guildId, DateTime sentAt, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes dispatch records older than the given cutoff so the table does not grow forever.
    /// </summary>
    /// <param name="cutoff">Records strictly older than this instant are deleted.</param>
    /// <param name="cancellationToken">Cancellation token to observe.</param>
    /// <returns>A task representing the asynchronous cleanup operation.</returns>
    Task PruneAsync(DateTime cutoff, CancellationToken cancellationToken = default);
}
