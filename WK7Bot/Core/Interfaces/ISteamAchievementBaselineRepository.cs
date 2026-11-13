namespace WK7Bot.Core.Interfaces;

using WK7Bot.Core.Entities;

/// <summary>
/// Repository contract for persisted Steam achievement baselines used by the achievement unlock digest.
/// </summary>
public interface ISteamAchievementBaselineRepository
{
    /// <summary>
    /// Returns baseline rows, optionally filtered by Discord user, Steam ID, app, and achievement API name.
    /// </summary>
    /// <param name="discordUserId">Optional Discord user filter.</param>
    /// <param name="steamId">Optional Steam ID filter.</param>
    /// <param name="appId">Optional app filter.</param>
    /// <param name="apiName">Optional achievement API name filter.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The matching baseline rows.</returns>
    Task<IReadOnlyList<SteamAchievementBaseline>> GetBaselineAsync(
        ulong? discordUserId = null,
        string? steamId = null,
        uint? appId = null,
        string? apiName = null,
        CancellationToken cancellationToken = default);

    /// <summary>Inserts or updates the supplied baseline rows.</summary>
    Task<int> UpsertManyAsync(IEnumerable<SteamAchievementBaseline> baselines, CancellationToken cancellationToken = default);
}