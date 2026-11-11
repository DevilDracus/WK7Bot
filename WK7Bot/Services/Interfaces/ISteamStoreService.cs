namespace WK7Bot.Services.Interfaces;

/// <summary>
/// Defines operations against the public Steam store API for resolving games and fetching store metadata.
/// </summary>
public interface ISteamStoreService
{
    /// <summary>
    /// Searches the Steam store for a game by name and returns the best match.
    /// </summary>
    /// <param name="gameName">The game name to search for.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The AppID and display name of the best match, or null when no match is found.</returns>
    Task<(uint AppId, string Title)?> SearchGameAsync(string gameName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetches store details for a Steam AppID, including the release date and "coming soon" flag.
    /// </summary>
    /// <param name="appId">The Steam application identifier.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The release date metadata, or null when the app cannot be resolved.</returns>
    Task<SteamStoreAppDetails?> GetAppDetailsAsync(uint appId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Release-date metadata returned by the Steam store appdetails endpoint.
/// </summary>
public sealed record SteamStoreAppDetails(
    uint AppId,
    string Name,
    DateTime? ReleaseDate,
    bool IsTba,
    bool IsComingSoon);
