namespace WK7Bot.Services;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WK7Bot.Core.Utilities;
using WK7Bot.Models;
using WK7Bot.Options;
using WK7Bot.Services.Interfaces;

/// <summary>
/// Queries Steam wishlists and IsThereAnyDeal price data to evaluate deal alerts for mapped users.
/// </summary>
public sealed class WishlistDealService : IWishlistDealService
{
    private const string SteamWishlistEndpoint = "https://store.steampowered.com/wishlist/profiles/{0}/wishlistdata/";
    private const string SteamWishlistDataSuffix = "wishlistdata/";

    /// <summary>
    /// Resolves the Steam wishlist data URL. If a shared wishlist URL is provided, the data endpoint
    /// is derived from it (appending <c>wishlistdata/</c> when missing) so that wishlists shared via
    /// a link can be fetched even when the Steam profile itself is private. Falls back to the
    /// SteamID-based endpoint when no URL is supplied.
    /// </summary>
    /// <param name="steamId">The 64-bit Steam ID of the user.</param>
    /// <param name="wishlistUrl">An optional shared wishlist URL.</param>
    /// <returns>The wishlist data URL to fetch.</returns>
    private static string ResolveWishlistUrl(string steamId, string? wishlistUrl)
    {
        if (string.IsNullOrWhiteSpace(wishlistUrl))
        {
            return string.Format(SteamWishlistEndpoint, steamId);
        }

        var trimmed = wishlistUrl.Trim().TrimEnd('/');
        if (trimmed.EndsWith(SteamWishlistDataSuffix.TrimEnd('/'), StringComparison.OrdinalIgnoreCase))
        {
            return trimmed + "/";
        }

        return $"{trimmed}/{SteamWishlistDataSuffix}";
    }
    private const string ItadLookupEndpoint = "https://api.isthereanydeal.com/games/lookup/v1";
    private const string ItadPricesEndpoint = "https://api.isthereanydeal.com/games/prices/v2";
    private const int MaxAttempts = 3;

    private readonly HttpClient _httpClient;
    private readonly Wk7BotOptions _options;
    private readonly ILogger<WishlistDealService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="WishlistDealService"/> class.
    /// </summary>
    /// <param name="httpClient">The HTTP client for external API requests.</param>
    /// <param name="options">The application options containing discount thresholds.</param>
    /// <param name="logger">The logger instance.</param>
    public WishlistDealService(HttpClient httpClient, IOptions<Wk7BotOptions> options, ILogger<WishlistDealService> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SteamWishlistItem>> FetchWishlistAsync(string steamId, string? wishlistUrl = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(steamId);

        var url = ResolveWishlistUrl(steamId, wishlistUrl);

        try
        {
            using var response = await GetWithRetryAsync(url, cancellationToken);
            if (response.StatusCode == HttpStatusCode.Forbidden)
            {
                _logger.LogWarning("Steam wishlist for {SteamId} is private or unavailable (403).", steamId);
                return Array.Empty<SteamWishlistItem>();
            }

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Steam wishlist request for {SteamId} returned {StatusCode}.", steamId, response.StatusCode);
                return Array.Empty<SteamWishlistItem>();
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(json) || json.Trim() == "[]")
            {
                return Array.Empty<SteamWishlistItem>();
            }

            // Steam occasionally returns HTML (rate limit, maintenance, login page) with 200 OK.
            if (json.TrimStart().StartsWith('<'))
            {
                _logger.LogWarning(
                    "Steam wishlist for {SteamId} returned HTML instead of JSON (likely rate-limited or unavailable).",
                    steamId);
                return Array.Empty<SteamWishlistItem>();
            }

            var wishlist = JsonSerializer.Deserialize<SteamWishlistResponse>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (wishlist == null || wishlist.Count == 0)
            {
                return Array.Empty<SteamWishlistItem>();
            }

            return wishlist.Values.ToList();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch Steam wishlist for {SteamId}.", steamId);
            return Array.Empty<SteamWishlistItem>();
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<uint, ItadPriceData>> FetchPricesAsync(IEnumerable<uint> appIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(appIds);

        var appIdList = appIds.Distinct().ToList();
        if (appIdList.Count == 0)
        {
            return new Dictionary<uint, ItadPriceData>();
        }

        var results = new Dictionary<uint, ItadPriceData>();

        // ITAD lookup: resolve Steam app IDs to ITAD game IDs
        var lookupTasks = appIdList.Select(async appId =>
        {
            try
            {
                var url = $"{ItadLookupEndpoint}?key=free&appid={appId}";
                using var response = await GetWithRetryAsync(url, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    return (appId: appId, itadId: (string?)null);
                }

                var lookup = await response.Content.ReadFromJsonAsync<ItadLookupResponse>(cancellationToken: cancellationToken);
                return (appId: appId, itadId: lookup?.Game?.Id);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogDebug(ex, "ITAD lookup failed for Steam app {AppId}.", appId);
                return (appId: appId, itadId: (string?)null);
            }
        });

        var lookups = await Task.WhenAll(lookupTasks);
        var itadIdByAppId = lookups
            .Where(x => x.itadId != null)
            .ToDictionary(x => x.appId, x => x.itadId!);

        if (itadIdByAppId.Count == 0)
        {
            return results;
        }

        // ITAD prices: query current prices for resolved game IDs
        var priceUrl = $"{ItadPricesEndpoint}?key=free&nondeals=false";
        var pricePayload = itadIdByAppId.Values.Select(id => new { id }).ToList();

        try
        {
            using var priceResponse = await PostWithRetryAsync(priceUrl, pricePayload, cancellationToken);
            if (!priceResponse.IsSuccessStatusCode)
            {
                _logger.LogWarning("ITAD prices request returned {StatusCode}.", priceResponse.StatusCode);
                return results;
            }

            var priceData = await priceResponse.Content.ReadFromJsonAsync<List<ItadPriceResponse>>(cancellationToken: cancellationToken);
            if (priceData == null)
            {
                return results;
            }

            foreach (var item in priceData)
            {
                if (item.Deals == null || item.Deals.Count == 0)
                {
                    continue;
                }

                var bestDeal = item.Deals.OrderBy(d => d.Price.Amount).First();
                var appId = itadIdByAppId.FirstOrDefault(x => x.Value == item.Game.Id).Key;
                if (appId == 0)
                {
                    continue;
                }

                results[appId] = new ItadPriceData
                {
                    Title = item.Game.Title,
                    CurrentPrice = bestDeal.Price.Amount,
                    RegularPrice = bestDeal.Regular.Amount,
                    HistoricalLow = item.LowestPrice?.Amount ?? bestDeal.Price.Amount,
                    StoreName = bestDeal.Shop.Name,
                    DealUrl = bestDeal.Url
                };
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch ITAD prices.");
        }

        return results;
    }

    /// <inheritdoc />
    public WishlistDealAlert? EvaluateAlert(uint appId, string gameTitle, ItadPriceData priceData, IReadOnlyList<ulong> notifiedUserIds)
    {
        if (priceData == null)
        {
            return null;
        }

        var minDiscount = _options.DealMinDiscountPercent;
        var meetsThreshold = priceData.IsHistoricalLow || priceData.DiscountPercent >= minDiscount;

        if (!meetsThreshold)
        {
            return null;
        }

        return new WishlistDealAlert
        {
            AppId = appId,
            GameTitle = gameTitle,
            CurrentPrice = priceData.CurrentPrice,
            RegularPrice = priceData.RegularPrice,
            HistoricalLow = priceData.HistoricalLow,
            DiscountPercent = priceData.DiscountPercent,
            IsHistoricalLow = priceData.IsHistoricalLow,
            StoreName = priceData.StoreName,
            DealUrl = priceData.DealUrl,
            NotifiedUserIds = notifiedUserIds.ToList()
        };
    }

    internal async Task<HttpResponseMessage> GetWithRetryAsync(string url, CancellationToken cancellationToken = default)
        => await ExecuteWithRetryAsync(() => _httpClient.GetAsync(url, cancellationToken), cancellationToken);

    internal async Task<HttpResponseMessage> PostWithRetryAsync(string url, object payload, CancellationToken cancellationToken = default)
        => await ExecuteWithRetryAsync(
            async () =>
            {
                using var content = JsonContent.Create(payload);
                return await _httpClient.PostAsync(url, content, cancellationToken);
            },
            cancellationToken);

    internal async Task<HttpResponseMessage> ExecuteWithRetryAsync(Func<Task<HttpResponseMessage>> sendFactory, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sendFactory);

        return await HttpRetry.ExecuteAsync(
            sendFactory,
            MaxAttempts,
            response => !response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.Forbidden,
            attempt => TimeSpan.FromSeconds(attempt),
            null,
            null,
            cancellationToken);
    }
}

// ITAD API response models

internal sealed class ItadLookupResponse
{
    [JsonPropertyName("game")]
    public ItadGameRef? Game { get; set; }
}

internal sealed class ItadGameRef
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;
}

internal sealed class ItadPriceResponse
{
    [JsonPropertyName("game")]
    public ItadGameInfo Game { get; set; } = new();

    [JsonPropertyName("deals")]
    public List<ItadDeal>? Deals { get; set; }

    [JsonPropertyName("lowest")]
    public ItadPricePoint? LowestPrice { get; set; }
}

internal sealed class ItadGameInfo
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;
}

internal sealed class ItadDeal
{
    [JsonPropertyName("shop")]
    public ItadShop Shop { get; set; } = new();

    [JsonPropertyName("price")]
    public ItadPricePoint Price { get; set; } = new();

    [JsonPropertyName("regular")]
    public ItadPricePoint Regular { get; set; } = new();

    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;
}

internal sealed class ItadShop
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
}

internal sealed class ItadPricePoint
{
    [JsonPropertyName("amount")]
    public decimal Amount { get; set; }
}
