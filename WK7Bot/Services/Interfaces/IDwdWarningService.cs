namespace WK7Bot.Services.Interfaces;

using WK7Bot.Models;

/// <summary>
/// Provides the current official weather warnings published by the German Weather Service (DWD)
/// as CAP alerts, used to trigger local rain and storm notifications.
/// </summary>
public interface IDwdWarningService
{
    /// <summary>
    /// Fetches the newest DWD warning snapshot and returns every parsed alert it contains.
    /// An empty snapshot (no active warnings) yields an empty list.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for network operations.</param>
    /// <returns>The warnings of the newest snapshot.</returns>
    Task<IReadOnlyList<CapWarning>> FetchWarningsAsync(CancellationToken cancellationToken = default);
}
