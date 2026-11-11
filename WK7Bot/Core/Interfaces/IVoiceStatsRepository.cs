namespace WK7Bot.Core.Interfaces;

using WK7Bot.Core.Entities;

/// <summary>
/// Data access for voice-channel session tracking.
/// </summary>
public interface IVoiceStatsRepository
{
    /// <summary>Records a completed voice session.</summary>
    /// <param name="session">The session to store.</param>
    /// <param name="cancellationToken">Cancellation token to observe.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task AddAsync(VoiceSession session, CancellationToken cancellationToken = default);

    /// <summary>Returns the total voice time (in seconds) for a user in a guild.</summary>
    /// <param name="discordUserId">The Discord user.</param>
    /// <param name="guildId">The guild.</param>
    /// <param name="cancellationToken">Cancellation token to observe.</param>
    /// <returns>Total seconds spent in voice.</returns>
    Task<int> GetTotalSecondsAsync(ulong discordUserId, ulong guildId, CancellationToken cancellationToken = default);
}
