namespace WK7Bot.Services.Interfaces;

using WK7Bot.Models;

/// <summary>
/// Queries the Open Food Facts database by product name for nutritional data and renal safety assessment.
/// </summary>
public interface IOpenFoodFactsService
{
    /// <summary>
    /// Searches for a food product by name and returns the best match with nutritional data.
    /// </summary>
    /// <param name="query">The product name to search for (e.g. "Banane").</param>
    /// <param name="cancellationToken">A token to monitor for task cancellation.</param>
    /// <returns>
    /// The top matching product with nutritional data, or <see langword="null"/> when no match is found.
    /// </returns>
    Task<OpenFoodFactsProduct?> SearchProductAsync(string query, CancellationToken cancellationToken = default);
}
