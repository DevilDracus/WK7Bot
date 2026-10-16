namespace WK7Bot.Services.Interfaces;

/// <summary>
/// Builds Steam and Battle.net leaderboards for the linked members of a Discord guild.
/// </summary>
public interface ILeaderboardService
{
    /// <summary>
    /// Builds the requested leaderboard.
    /// </summary>
    /// <param name="type">One of <c>steam-playtime</c>, <c>steam-achievements</c>, <c>battlenet-wow</c>, <c>battlenet-diablo</c>.</param>
    /// <param name="guildId">The Discord guild used to resolve member display names.</param>
    /// <param name="limit">Maximum number of entries.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The leaderboard result.</returns>
    Task<LeaderboardResult> GetAsync(string type, ulong guildId, int limit, CancellationToken cancellationToken = default);
}

/// <summary>A single leaderboard row.</summary>
/// <param name="Rank">The 1-based rank.</param>
/// <param name="DisplayName">The Discord display name (or fallback).</param>
/// <param name="Value">The metric value.</param>
/// <param name="Detail">Optional supporting detail.</param>
public sealed record LeaderboardEntry(int Rank, string DisplayName, string Value, string? Detail = null);

/// <summary>A rendered leaderboard.</summary>
/// <param name="Title">The embed title.</param>
/// <param name="Entries">The ranked entries.</param>
/// <param name="Footer">Optional footer text.</param>
public sealed record LeaderboardResult(string Title, IReadOnlyList<LeaderboardEntry> Entries, string? Footer = null);
