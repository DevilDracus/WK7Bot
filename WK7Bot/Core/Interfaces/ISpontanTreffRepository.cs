using WK7Bot.Core.Entities;

namespace WK7Bot.Core.Interfaces;

/// <summary>
/// Defines data access operations for spontaneous meetups ("Spontan-Treff") and their button answers.
/// </summary>
public interface ISpontanTreffRepository
{
    /// <summary>
    /// Stores a new meetup and assigns its database identifier.
    /// </summary>
    /// <param name="meetup">The meetup to persist.</param>
    /// <param name="cancellationToken">Cancellation token to observe.</param>
    /// <returns>The persisted meetup including its generated identifier.</returns>
    Task<SpontanTreff> AddAsync(SpontanTreff meetup, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a meetup that could not be posted, including its stored answers.
    /// </summary>
    /// <param name="meetupId">The meetup identifier.</param>
    /// <param name="cancellationToken">Cancellation token to observe.</param>
    /// <returns>A task representing the asynchronous removal.</returns>
    Task RemoveAsync(int meetupId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores the channel and message location of a meetup once it has been posted.
    /// </summary>
    /// <param name="meetupId">The meetup identifier.</param>
    /// <param name="channelId">The channel containing the meetup message.</param>
    /// <param name="messageId">The interactive meetup message identifier.</param>
    /// <param name="cancellationToken">Cancellation token to observe.</param>
    Task SetMessageAsync(int meetupId, ulong channelId, ulong messageId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves a meetup by its identifier.
    /// </summary>
    /// <param name="meetupId">The meetup identifier.</param>
    /// <param name="cancellationToken">Cancellation token to observe.</param>
    /// <returns>The matching meetup, or <see langword="null"/> when it does not exist.</returns>
    Task<SpontanTreff?> GetAsync(int meetupId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves all answers for a meetup ordered by response time.
    /// </summary>
    /// <param name="meetupId">The meetup identifier.</param>
    /// <param name="cancellationToken">Cancellation token to observe.</param>
    /// <returns>The stored answers of the meetup.</returns>
    Task<List<SpontanTreffResponse>> GetResponsesAsync(int meetupId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves the answer a single user gave for a meetup.
    /// </summary>
    /// <param name="meetupId">The meetup identifier.</param>
    /// <param name="userId">The Discord user identifier.</param>
    /// <param name="cancellationToken">Cancellation token to observe.</param>
    /// <returns>The stored answer, or <see langword="null"/> when the user has not answered yet.</returns>
    Task<SpontanTreffResponse?> GetResponseAsync(int meetupId, ulong userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Inserts or replaces the answer of a user for a meetup.
    /// </summary>
    /// <param name="response">The answer to persist.</param>
    /// <param name="cancellationToken">Cancellation token to observe.</param>
    Task SetResponseAsync(SpontanTreffResponse response, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a stored answer so the user is neither counted as attending nor as passed.
    /// </summary>
    /// <param name="meetupId">The meetup identifier.</param>
    /// <param name="userId">The Discord user identifier.</param>
    /// <param name="cancellationToken">Cancellation token to observe.</param>
    Task RemoveResponseAsync(int meetupId, ulong userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks a meetup as closed so it is never processed by the expiry sweep again.
    /// </summary>
    /// <param name="meetupId">The meetup identifier.</param>
    /// <param name="cancellationToken">Cancellation token to observe.</param>
    Task CloseAsync(int meetupId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves all meetups whose deadline has passed and that are not closed yet.
    /// </summary>
    /// <param name="now">The current local time.</param>
    /// <param name="cancellationToken">Cancellation token to observe.</param>
    /// <returns>The meetups waiting to be expired.</returns>
    Task<List<SpontanTreff>> GetExpiredAsync(DateTime now, CancellationToken cancellationToken = default);
}
