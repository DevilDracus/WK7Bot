namespace WK7Bot.Core.Interfaces;

using WK7Bot.Core.Entities;

/// <summary>
/// Repository contract for per-user game release-date reminders.
/// </summary>
public interface IReleaseDateReminderRepository
{
    /// <summary>Returns every release-date reminder.</summary>
    Task<IReadOnlyList<ReleaseDateReminderItem>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns the release-date reminders for one Discord user.</summary>
    Task<IReadOnlyList<ReleaseDateReminderItem>> GetForUserAsync(ulong discordUserId, CancellationToken cancellationToken = default);

    /// <summary>Adds a new release-date reminder.</summary>
    Task AddAsync(ReleaseDateReminderItem item, CancellationToken cancellationToken = default);

    /// <summary>Removes a release-date reminder by user and AppID.</summary>
    Task<bool> RemoveAsync(ulong discordUserId, uint appId, CancellationToken cancellationToken = default);

    /// <summary>Updates the release date and last-notification timestamp for an entry.</summary>
    Task UpdateAsync(ReleaseDateReminderItem item, CancellationToken cancellationToken = default);
}
