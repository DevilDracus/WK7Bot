namespace WK7Bot.Core.Interfaces;

using WK7Bot.Core.Entities;

/// <summary>
/// Repository contract for persisted Steam achievement baselines used by the achievement unlock digest.
/// </summary>
public interface ISteamAchievementBaselineRepository
{
    /// <summary>Returns all baseline rows.</summary>
    Task<IReadOnlyList<SteamAchievementBaseline>> GetBaselineAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns baseline rows for the given Discord user.</summary>
    Task<IReadOnlyList<SteamAchievementBaseline>> GetBaselineAsync(ulong discordUserId, CancellationToken cancellationToken = default);

    /// <summary>Returns baseline rows for the given Steam ID.</summary>
    Task<IReadOnlyList<SteamAchievementBaseline>> GetBaselineAsync(string steamId, CancellationToken cancellationToken = default);

    /// <summary>Returns baseline rows for the given Discord user and Steam ID.</summary>
    Task<IReadOnlyList<SteamAchievementBaseline>> GetBaselineAsync(ulong discordUserId, string steamId, CancellationToken cancellationToken = default);

    /// <summary>Returns baseline rows for the given Discord user, Steam ID and game.</summary>
    Task<IReadOnlyList<SteamAchievementBaseline>> GetBaselineAsync(ulong discordUserId, string steamId, uint appId, CancellationToken cancellationToken = default);

    /// <summary>Returns the baseline row for a single achievement, or <see langword="null" />.</summary>
    Task<SteamAchievementBaseline?> GetBaselineAsync(ulong discordUserId, string steamId, uint appId, string apiName, CancellationToken cancellationToken = default);

    /// <summary>Inserts or updates the supplied baseline rows.</summary>
    Task<int> UpsertManyAsync(IEnumerable<SteamAchievementBaseline> baselines, CancellationToken cancellationToken = default);
}
