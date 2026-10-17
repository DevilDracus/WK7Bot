namespace WK7Bot.Core.Interfaces;

using WK7Bot.Core.Entities;

/// <summary>
/// Repository contract for Discord guild scheduled events created by the bot.
/// </summary>
public interface IDiscordScheduledEventRepository
{
    /// <summary>Stores a newly created scheduled event.</summary>
    Task AddAsync(DiscordScheduledEvent item, CancellationToken cancellationToken = default);

    /// <summary>Returns every tracked event.</summary>
    Task<IReadOnlyList<DiscordScheduledEvent>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns the events whose end time is at or before <paramref name="now"/>.</summary>
    Task<IReadOnlyList<DiscordScheduledEvent>> GetExpiredAsync(DateTime now, CancellationToken cancellationToken = default);

    /// <summary>Removes a tracked event row.</summary>
    Task RemoveAsync(int id, CancellationToken cancellationToken = default);
}
