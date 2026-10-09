namespace WK7Bot.Services;

using System;
using System.Threading;
using System.Threading.Tasks;
using WK7Bot.Core.Utilities;
using WK7Bot.Models;
using WK7Bot.Services.Interfaces;

/// <summary>
/// Caches <see cref="BattleNetUserData"/> lookups per Battle.net refresh token for a short
/// time-to-live so rapid Discord presence update storms do not hammer the Blizzard profile and
/// OAuth endpoints with duplicate requests. Concurrent misses for the same account share a single
/// request (see <see cref="SingleFlightCache{TValue}"/>).
/// </summary>
public class BattleNetDataCache : SingleFlightCache<BattleNetUserData>
{
    private readonly IBattleNetService _battleNetService;

    /// <summary>
    /// Initializes a new instance of the <see cref="BattleNetDataCache"/> class.
    /// </summary>
    /// <param name="battleNetService">The underlying Battle.net service used on cache misses.</param>
    /// <param name="timeToLive">How long a cached entry remains valid.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="battleNetService"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="timeToLive"/> is not positive.</exception>
    public BattleNetDataCache(IBattleNetService battleNetService, TimeSpan timeToLive)
        : base(timeToLive)
    {
        _battleNetService = battleNetService ?? throw new ArgumentNullException(nameof(battleNetService));
    }

    /// <summary>
    /// Returns cached Battle.net account data when younger than the time-to-live; otherwise fetches fresh data.
    /// </summary>
    /// <param name="refreshToken">The OAuth refresh token identifying the mapped account.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>The cached or freshly fetched <see cref="BattleNetUserData"/>, or null when retrieval fails.</returns>
    public Task<BattleNetUserData?> GetBattleNetUserDataAsync(string refreshToken, CancellationToken cancellationToken = default)
        => GetAsync(refreshToken, cancellationToken);

    /// <summary>
    /// Returns cached Battle.net account data when younger than the time-to-live, or fetches fresh data when the
    /// entry expired or when <paramref name="forceRefresh"/> is set. Concurrent cache misses for the same account
    /// share one in-flight request.
    /// </summary>
    /// <param name="refreshToken">The OAuth refresh token identifying the mapped account.</param>
    /// <param name="forceRefresh">When true, a still-fresh cached entry is bypassed and the account is queried again.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>The cached or freshly fetched <see cref="BattleNetUserData"/>, or null when retrieval fails.</returns>
    public Task<BattleNetUserData?> GetBattleNetUserDataAsync(string refreshToken, bool forceRefresh, CancellationToken cancellationToken = default)
        => GetAsync(refreshToken, forceRefresh, cancellationToken);

    /// <inheritdoc/>
    protected override Task<BattleNetUserData?> FetchAsync(string refreshToken, CancellationToken cancellationToken)
        => _battleNetService.GetBattleNetUserDataAsync(refreshToken, cancellationToken);
}
