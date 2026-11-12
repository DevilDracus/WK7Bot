#pragma warning disable CS1591
#pragma warning disable CS1587
using WK7Bot.Core.Entities;

namespace WK7Bot.Core.Interfaces;

/// <summary>
/// Defines data access operations for game nights, their candidate slots and availability votes.
/// </summary>
public interface IGameNightRepository
{
    Task<GameNight> AddAsync(GameNight gameNight, CancellationToken cancellationToken = default);
    Task RemoveAsync(int gameNightId, CancellationToken cancellationToken = default);
    Task SetMessageAsync(int gameNightId, ulong channelId, ulong messageId, CancellationToken cancellationToken = default);
    Task<GameNight?> GetAsync(int gameNightId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<GameNight>> GetExpiredAsync(DateTime now, CancellationToken cancellationToken = default);
    Task CloseAsync(int gameNightId, CancellationToken cancellationToken = default);
    Task CloseManyAsync(IEnumerable<int> gameNightIds, CancellationToken cancellationToken = default);

    Task<GameNightSlot> AddSlotAsync(GameNightSlot slot, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<GameNightSlot>> AddSlotsAsync(int gameNightId, IEnumerable<string> slotLabels, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<GameNightSlot>> GetSlotsAsync(int gameNightId, CancellationToken cancellationToken = default);
    Task<GameNightSlot?> GetSlotAsync(int slotId, CancellationToken cancellationToken = default);

    Task<GameNightVote?> GetVoteAsync(int gameNightId, int slotId, ulong userId, CancellationToken cancellationToken = default);
    Task<GameNightVote?> GetResponseAsync(int gameNightId, int slotId, ulong userId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<GameNightVote>> GetVotesAsync(int gameNightId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<GameNightVote>> GetVotesForSlotAsync(int slotId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<GameNightVote>> GetResponsesAsync(int gameNightId, CancellationToken cancellationToken = default);
    Task SetVoteAsync(GameNightVote vote, CancellationToken cancellationToken = default);
    Task SetResponseAsync(GameNightVote vote, CancellationToken cancellationToken = default);
    Task RemoveVoteAsync(int gameNightId, int slotId, ulong userId, CancellationToken cancellationToken = default);
    Task RemoveResponseAsync(int gameNightId, int slotId, ulong userId, CancellationToken cancellationToken = default);

    Task SetBestSlotAsync(int gameNightId, int slotId, CancellationToken cancellationToken = default);
}
