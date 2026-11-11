namespace WK7Bot.Core.Interfaces;

using WK7Bot.Core.Entities;

/// <summary>
/// Data access for member birthdays.
/// </summary>
public interface IBirthdayRepository
{
    /// <summary>Stores or updates a member's birthday.</summary>
    /// <param name="birthday">The birthday to upsert.</param>
    /// <param name="cancellationToken">Cancellation token to observe.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task UpsertAsync(Birthday birthday, CancellationToken cancellationToken = default);

    /// <summary>Returns the birthdays that fall on the given month/day and have not yet been announced this year.</summary>
    /// <param name="month">The month (1-12).</param>
    /// <param name="day">The day (1-31).</param>
    /// <param name="year">The current year used for de-duplication.</param>
    /// <param name="cancellationToken">Cancellation token to observe.</param>
    /// <returns>The matching birthdays.</returns>
    Task<IReadOnlyList<Birthday>> GetDueTodayAsync(int month, int day, int year, CancellationToken cancellationToken = default);

    /// <summary>Returns the birthday of one user, if any.</summary>
    /// <param name="discordUserId">The owning Discord user.</param>
    /// <param name="cancellationToken">Cancellation token to observe.</param>
    /// <returns>The birthday or <see langword="null"/>.</returns>
    Task<Birthday?> GetForUserAsync(ulong discordUserId, CancellationToken cancellationToken = default);

    /// <summary>Marks a birthday as announced for the given year.</summary>
    /// <param name="id">The birthday identifier.</param>
    /// <param name="year">The year it was announced in.</param>
    /// <param name="cancellationToken">Cancellation token to observe.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task MarkAnnouncedAsync(int id, int year, CancellationToken cancellationToken = default);

    /// <summary>Removes a member's birthday.</summary>
    /// <param name="discordUserId">The owning Discord user.</param>
    /// <param name="cancellationToken">Cancellation token to observe.</param>
    /// <returns><see langword="true"/> when a row was removed.</returns>
    Task<bool> RemoveAsync(ulong discordUserId, CancellationToken cancellationToken = default);
}
