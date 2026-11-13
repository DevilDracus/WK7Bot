namespace WK7Bot.Services;

using System.Net.Http.Json;
using System.Text.Json;
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

    /// <summary>Cache lifetime applied to negative (not-found) results, shorter than the hit cache.</summary>
    private static readonly TimeSpan NullResultCacheLifetime = TimeSpan.FromMinutes(5);

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

            using var document = await JsonDocument.ParseAsync(
                await response.Content.ReadAsStreamAsync(cancellationToken),
                cancellationToken: cancellationToken);

            var products = ExtractProducts(document.RootElement);
            var product = products.FirstOrDefault(p => !string.IsNullOrWhiteSpace(p.ProductName));

            if (product == null)
            {
                _logger.LogInformation("No Open Food Facts product found for query '{Query}'.", query);
                _cache.Set<OpenFoodFactsProduct?>(cacheKey, null, new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = NullResultCacheLifetime, Size = 1 });
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

    private static List<OpenFoodFactsProduct> ExtractProducts(JsonElement root)
    {
        List<OpenFoodFactsProduct> result = [];

        if (root.ValueKind == JsonValueKind.Null || root.ValueKind == JsonValueKind.Undefined)
        {
            return result;
        }

        if (root.ValueKind == JsonValueKind.Array)
        {
            foreach (var element in root.EnumerateArray())
            {
                if (element.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var product = JsonSerializer.Deserialize<OpenFoodFactsProduct>(element.GetRawText());
                if (product is not null)
                {
                    result.Add(product);
                }
            }

            return result;
        }

        if (root.ValueKind != JsonValueKind.Object)
        {
            return result;
        }

        if (root.TryGetProperty("products", out var productsElement))
        {
            return ExtractProducts(productsElement);
        }

        // Dictionary-style response: the object's values are product objects.
        foreach (var property in root.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            // Only treat objects that look like Open Food Facts products.
            if (!property.Value.TryGetProperty("product_name", out _) &&
                !property.Value.TryGetProperty("brands", out _) &&
                !property.Value.TryGetProperty("nutriments", out _))
            {
                continue;
            }

            var product = JsonSerializer.Deserialize<OpenFoodFactsProduct>(property.Value.GetRawText());
            if (product is not null)
            {
                result.Add(product);
            }
        }

        return result;
    }
}
