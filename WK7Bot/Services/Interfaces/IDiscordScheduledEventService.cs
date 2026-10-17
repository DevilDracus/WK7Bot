namespace WK7Bot.Services.Interfaces;

using Discord;

/// <summary>
/// Creates native Discord guild scheduled events for bot features and deletes them once they ended.
/// </summary>
public interface IDiscordScheduledEventService
{
    /// <summary>
    /// Creates an external guild scheduled event and records it for later cleanup.
    /// </summary>
    /// <param name="guild">The guild that owns the event.</param>
    /// <param name="name">The event name.</param>
    /// <param name="description">The optional description.</param>
    /// <param name="location">The external location text.</param>
    /// <param name="start">The start time.</param>
    /// <param name="end">The end time.</param>
    /// <param name="kind">The creating feature kind.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The created event ID, or <see langword="null"/> when creation was skipped or failed.</returns>
    Task<ulong?> CreateAsync(
        IGuild guild,
        string name,
        string? description,
        string? location,
        DateTimeOffset start,
        DateTimeOffset end,
        string kind,
        CancellationToken cancellationToken = default);

    /// <summary>Deletes every tracked event whose end time has passed.</summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The number of events deleted from Discord.</returns>
    Task<int> DeleteExpiredAsync(CancellationToken cancellationToken = default);
}
