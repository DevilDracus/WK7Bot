namespace WK7Bot.Core.Interfaces;

/// <summary>
/// Defines data access operations for persisting automatic food publication state so duplicate
/// posts are suppressed even after a bot restart.
/// </summary>
public interface IFoodDispatchRepository
{
    /// <summary>
    /// Determines whether a dispatch of the given kind was already sent on a specific date.
    /// </summary>
    /// <param name="kind">The dispatch kind (see <c>FoodDispatchKinds</c>).</param>
    /// <param name="sentOn">The calendar date the message would be sent on.</param>
    /// <param name="cancellationToken">Cancellation token to observe.</param>
    /// <returns><see langword="true"/> when the message was already sent; otherwise <see langword="false"/>.</returns>
    Task<bool> HasSentAsync(string kind, DateTime sentOn, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records that a dispatch of the given kind was sent on a specific date. Idempotent.
    /// </summary>
    /// <param name="kind">The dispatch kind (see <c>FoodDispatchKinds</c>).</param>
    /// <param name="sentOn">The calendar date the message was sent on.</param>
    /// <param name="cancellationToken">Cancellation token to observe.</param>
    /// <returns>A task representing the asynchronous storage operation.</returns>
    Task MarkSentAsync(string kind, DateTime sentOn, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes dispatch records older than the given retention window so the table stays bounded.
    /// </summary>
    /// <param name="olderThan">Records strictly older than this date are removed.</param>
    /// <param name="cancellationToken">Cancellation token to observe.</param>
    /// <returns>The number of records removed.</returns>
    Task<int> PruneAsync(DateTime olderThan, CancellationToken cancellationToken = default);
}
