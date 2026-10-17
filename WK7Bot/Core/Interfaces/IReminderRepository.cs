namespace WK7Bot.Core.Interfaces;

using WK7Bot.Core.Entities;

/// <summary>
/// Data access for personal reminders.
/// </summary>
public interface IReminderRepository
{
    /// <summary>Stores a new reminder.</summary>
    /// <param name="reminder">The reminder to insert.</param>
    /// <param name="cancellationToken">Cancellation token to observe.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task AddAsync(Reminder reminder, CancellationToken cancellationToken = default);

    /// <summary>Returns every undelivered reminder whose due time has passed.</summary>
    /// <param name="nowUtc">The current UTC time.</param>
    /// <param name="cancellationToken">Cancellation token to observe.</param>
    /// <returns>The due reminders, oldest first.</returns>
    Task<IReadOnlyList<Reminder>> GetDueAsync(DateTime nowUtc, CancellationToken cancellationToken = default);

    /// <summary>Returns the open reminders of one user.</summary>
    /// <param name="discordUserId">The owning Discord user.</param>
    /// <param name="cancellationToken">Cancellation token to observe.</param>
    /// <returns>The user's pending reminders, soonest first.</returns>
    Task<IReadOnlyList<Reminder>> GetPendingForUserAsync(ulong discordUserId, CancellationToken cancellationToken = default);

    /// <summary>Marks a reminder as delivered.</summary>
    /// <param name="id">The reminder identifier.</param>
    /// <param name="cancellationToken">Cancellation token to observe.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task MarkDeliveredAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>Removes a pending reminder owned by the given user.</summary>
    /// <param name="id">The reminder identifier.</param>
    /// <param name="discordUserId">The owning Discord user; used so one member cannot cancel another's reminder.</param>
    /// <param name="cancellationToken">Cancellation token to observe.</param>
    /// <returns><see langword="true"/> when a row was removed.</returns>
    Task<bool> RemovePendingAsync(int id, ulong discordUserId, CancellationToken cancellationToken = default);

    /// <summary>Removes delivered reminders older than the given UTC timestamp.</summary>
    /// <param name="olderThanUtc">Delivered reminders with an earlier delivery timestamp are removed.</param>
    /// <param name="cancellationToken">Cancellation token to observe.</param>
    /// <returns>The number of removed rows.</returns>
    Task<int> PruneDeliveredAsync(DateTime olderThanUtc, CancellationToken cancellationToken = default);
}
