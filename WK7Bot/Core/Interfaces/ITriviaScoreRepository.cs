namespace WK7Bot.Core.Interfaces;

using WK7Bot.Core.Entities;

/// <summary>
/// Data access for trivia scores.
/// </summary>
public interface ITriviaScoreRepository
{
    /// <summary>Increments (or creates) the score for a user in the given week.</summary>
    /// <param name="discordUserId">The Discord user.</param>
    /// <param name="guildId">The guild.</param>
    /// <param name="weekStart">The Monday (UTC) that starts the scoring week.</param>
    /// <param name="points">Points to add.</param>
    /// <param name="cancellationToken">Cancellation token to observe.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task IncrementAsync(ulong discordUserId, ulong guildId, DateTime weekStart, int points, CancellationToken cancellationToken = default);

    /// <summary>Returns the top scores for a guild and week.</summary>
    /// <param name="guildId">The guild.</param>
    /// <param name="weekStart">The Monday (UTC) that starts the scoring week.</param>
    /// <param name="limit">Maximum number of entries.</param>
    /// <param name="cancellationToken">Cancellation token to observe.</param>
    /// <returns>The top scores, highest first.</returns>
    Task<IReadOnlyList<TriviaScore>> GetTopAsync(ulong guildId, DateTime weekStart, int limit, CancellationToken cancellationToken = default);

    /// <summary>Removes scores older than the given week start.</summary>
    /// <param name="olderThanWeekStart">Scores with an earlier week start are removed.</param>
    /// <param name="cancellationToken">Cancellation token to observe.</param>
    /// <returns>The number of removed rows.</returns>
    Task<int> PruneOlderThanAsync(DateTime olderThanWeekStart, CancellationToken cancellationToken = default);
}
