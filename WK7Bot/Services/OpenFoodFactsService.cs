namespace WK7Bot.Services;

using System.Net.Http.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using WK7Bot.Core.Utilities;
using WK7Bot.Models;
using WK7Bot.Services.Interfaces;

/// <summary>
/// Queries the Open Food Facts public API by product name. No API key is required.
/// Results are cached in memory for 1 hour to reduce repeated lookups.
/// </summary>
public sealed class OpenFoodFactsService : IOpenFoodFactsService
{
    private const string SearchEndpoint = "https://world.openfoodfacts.org/cgi/search.pl";
    private const string UserAgent = "WK7Bot/1.0 (Discord bot)";
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromHours(1);

    private readonly HttpClient _httpClient;
    private readonly ILogger<OpenFoodFactsService> _logger;
    private readonly IMemoryCache _cache;

    /// <summary>
    /// Initializes a new instance of the <see cref="OpenFoodFactsService"/> class.
    /// </summary>
    /// <param name="httpClient">The HTTP client used for API requests.</param>
    /// <param name="logger">The logger instance.</param>
    /// <param name="cache">The memory cache instance holding results for one hour.</param>
    public OpenFoodFactsService(HttpClient httpClient, ILogger<OpenFoodFactsService> logger, IMemoryCache cache)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
    }

    /// <inheritdoc />
    public async Task<OpenFoodFactsProduct?> SearchProductAsync(string query, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return null;
        }

        var normalizedQuery = query.Trim();
        var cacheKey = $"openfoodfacts:{normalizedQuery.ToLowerInvariant()}";
        if (_cache.TryGetValue(cacheKey, out OpenFoodFactsProduct? cached))
        {
            return cached;
        }

        var url = $"{SearchEndpoint}?search_terms={Uri.EscapeDataString(normalizedQuery)}&search_simple=1&action=process&json=1&page_size=5";

        try
        {
            using var response = await HttpRetry.ExecuteAsync(
                async () =>
                {
                    var request = new HttpRequestMessage(HttpMethod.Get, url);
                    request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
                    return await _httpClient.SendAsync(request, cancellationToken);
                },
                3,
                HttpRetry.IsTransientResponse,
                HttpRetry.LinearBackoff,
                null,
                null,
                cancellationToken);

            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<OpenFoodFactsSearchResponse>(cancellationToken: cancellationToken);
            var product = result?.Products.FirstOrDefault(p => !string.IsNullOrWhiteSpace(p.ProductName));

            if (product == null)
            {
                _logger.LogInformation("No Open Food Facts product found for query '{Query}'.", query);
                _cache.Set<OpenFoodFactsProduct?>(cacheKey, null, TimeSpan.FromMinutes(5));
                return null;
            }
            _logger.LogInformation("Found product '{ProductName}' for query '{Query}'.", product.ProductName, query);
            _cache.Set(cacheKey, product, new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = CacheLifetime, Size = 1 });
            return product;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Open Food Facts search failed for query '{Query}'.", query);
            return null;
        }
    }
}
