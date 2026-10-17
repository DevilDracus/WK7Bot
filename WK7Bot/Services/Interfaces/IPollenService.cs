namespace WK7Bot.Services.Interfaces;

using WK7Bot.Models;

/// <summary>
/// Reads the DWD open-data pollen hazard index and UV index feeds (no API key required).
/// </summary>
public interface IPollenService
{
    /// <summary>Fetches the pollen forecast for the configured region.</summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The forecast, or <see langword="null"/> when the feed is unavailable or the region is missing.</returns>
    Task<PollenForecast?> GetPollenAsync(CancellationToken cancellationToken = default);

    /// <summary>Fetches the UV index forecast for the configured city.</summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The forecast, or <see langword="null"/> when the feed is unavailable or the city is missing.</returns>
    Task<UvForecast?> GetUvAsync(CancellationToken cancellationToken = default);
}
