namespace WK7Bot.Services.Interfaces;

using System.Threading;
using System.Threading.Tasks;
using WK7Bot.Models;

/// <summary>
/// Provides keyword based recipe lookup by searching the web and parsing structured recipe metadata (schema.org
/// JSON-LD) published by recipe sites. Unlike <see cref="IGeminiFoodService"/>, results are not generated and not
/// screened for dialysis or transplant safety.
/// </summary>
public interface IRecipeSearchService
{
    /// <summary>
    /// Searches the web for a recipe matching the free text query and parses the first candidate page that
    /// exposes structured recipe data.
    /// </summary>
    /// <param name="query">The free text search query (e.g. <c>Pfannkuchen</c>).</param>
    /// <param name="cancellationToken">A token to monitor for task cancellation.</param>
    /// <returns>
    /// A <see cref="RenalRecipeData"/> carrying the parsed recipe with <c>SourceUrl</c> and <c>ImageUrl</c> set,
    /// or <see langword="null"/> when no candidate exposes parseable recipe data.
    /// </returns>
    Task<RenalRecipeData?> SearchWebRecipeAsync(string query, CancellationToken cancellationToken = default);
}
