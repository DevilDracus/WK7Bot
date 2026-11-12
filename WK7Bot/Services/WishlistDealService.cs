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
    /// <summary>
    /// Resolves the Steam wishlist data URL. Steam's IWishlistService endpoint requires a public wishlist;
    /// shared links and private wishlists cannot be read programmatically.
    /// </summary>
    /// <param name="steamId">The 64-bit Steam ID of the user.</param>
    /// <param name="wishlistUrl">An optional shared Steam wishlist URL (stored for reference only).</param>
    /// <returns>The IWishlistService endpoint URL.</returns>
    private static string ResolveWishlistUrl(string steamId, string? wishlistUrl)
    {
        // Steam's IWishlistService endpoint requires a public wishlist.
        // Shared links and private wishlists cannot be read programmatically.
        _ = wishlistUrl; // Stored for reference only; not used for fetching.
        return $"https://api.steampowered.com/IWishlistService/GetWishlist/v1/?steamid={steamId}";
    }
    private const string ItadLookupEndpoint = "https://api.isthereanydeal.com/games/lookup/v1";
    private const string ItadPricesEndpoint = "https://api.isthereanydeal.com/games/prices/v3";
    private const int MaxAttempts = 3;

    /// <summary>Maximum number of ITAD game IDs per prices request (API limit).</summary>
    private const int ItadPriceChunkSize = 200;

    /// <summary>Expected length of a 64-bit Steam ID.</summary>
    private const int SteamId64Length = 17;

    /// <summary>Prefix shared by all 64-bit Steam IDs.</summary>
    private const string SteamId64Prefix = "7656119";

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

        if (!steamId.StartsWith(SteamId64Prefix, StringComparison.Ordinal) || steamId.Length != SteamId64Length)
        {
            _logger.LogWarning("Steam ID {SteamId} does not look like a 64-bit Steam ID; wishlist fetch may fail.", steamId);
        }

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

            // Try the IWishlistService format first: { "response": { "items": [...] } }
            var apiResponse = JsonSerializer.Deserialize<SteamWishlistApiResponse>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            if (apiResponse?.Response?.Items is { Count: > 0 })
            {
                return apiResponse.Response.Items;
            }

            // IWishlistService returns an empty response for private/empty wishlists.
            _logger.LogDebug("Steam wishlist for {SteamId} returned no items (private or empty).", steamId);
            return Array.Empty<SteamWishlistItem>();
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

        var apiKey = _options.ItadApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogWarning("No IsThereAnyDeal API key configured. Skipping price lookup. Register at https://isthereanydeal.com/developers/ to obtain a key.");
            return results;
        }

        // ITAD lookup: resolve Steam app IDs to ITAD game IDs
        var lookupTasks = appIdList.Select(async appId =>
        {
            var lookup = await LookupItadGameAsync(appId, cancellationToken).ConfigureAwait(false);
            if (lookup is null)
            {
                return (appId: appId, itadId: (string?)null, title: (string?)null);
            }

            return (appId: appId, itadId: lookup.Value.ItadId, title: lookup.Value.Title);
        });

        var lookups = await Task.WhenAll(lookupTasks);
        var lookupByAppId = lookups
            .Where(x => x.itadId != null)
            .ToDictionary(x => x.appId, x => (itadId: x.itadId!, title: x.title ?? string.Empty));

        if (lookupByAppId.Count == 0)
        {
            _logger.LogWarning("No ITAD game IDs resolved from {Count} Steam app IDs. Check API key and app IDs.", appIdList.Count);
            return results;
        }

        // ITAD prices: query current prices for resolved game IDs (chunked to respect the 200-id limit).
        var allItadIds = lookupByAppId.Values.Select(x => x.itadId).ToList();
        var priceUrl = $"{ItadPricesEndpoint}?key={apiKey}&country=DE";

        try
        {
            foreach (var chunk in Chunk(allItadIds, ItadPriceChunkSize))
            {
                using var priceResponse = await PostWithRetryAsync(priceUrl, chunk, cancellationToken);
                if (!priceResponse.IsSuccessStatusCode)
                {
                    _logger.LogWarning("ITAD prices request returned {StatusCode}.", priceResponse.StatusCode);
                    continue;
                }

                var priceData = await priceResponse.Content.ReadFromJsonAsync<List<ItadPriceResponse>>(cancellationToken: cancellationToken);
                if (priceData == null || priceData.Count == 0)
                {
                    continue;
                }

                foreach (var item in priceData)
                {
                    if (item.Deals == null || item.Deals.Count == 0)
                    {
                        continue;
                    }

                    var bestDeal = item.Deals.OrderBy(d => d.Price.Amount).First();
                    var match = lookupByAppId.FirstOrDefault(x => x.Value.itadId == item.Id);
                    if (match.Key == 0 && !lookupByAppId.ContainsKey(0))
                    {
                        continue;
                    }

                    var appId = match.Key;
                    var title = lookupByAppId.TryGetValue(appId, out var info) ? info.title : string.Empty;

                    results[appId] = new ItadPriceData
                    {
                        Title = title,
                        CurrentPrice = bestDeal.Price.Amount,
                        RegularPrice = bestDeal.Regular.Amount,
                        HistoricalLow = item.HistoryLow?.All?.Amount ?? bestDeal.Price.Amount,
                        StoreName = bestDeal.Shop.Name,
                        DealUrl = bestDeal.Url
                    };
                }
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
    public async Task<PriceComparisonResult?> ComparePricesAsync(uint appId, CancellationToken cancellationToken = default)
    {
        var lookup = await LookupItadGameAsync(appId, cancellationToken).ConfigureAwait(false);
        if (lookup is null)
        {
            return null;
        }

        var apiKey = _options.ItadApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogWarning("No IsThereAnyDeal API key configured. Skipping price comparison.");
            return null;
        }

        var priceUrl = $"{ItadPricesEndpoint}?key={apiKey}&country=DE";
        try
        {
            using var priceResponse = await PostWithRetryAsync(priceUrl, new[] { lookup.Value.ItadId }, cancellationToken).ConfigureAwait(false);
            if (!priceResponse.IsSuccessStatusCode)
            {
                _logger.LogWarning("ITAD prices request returned {StatusCode}.", priceResponse.StatusCode);
                return null;
            }

            var priceData = await priceResponse.Content.ReadFromJsonAsync<List<ItadPriceResponse>>(cancellationToken: cancellationToken).ConfigureAwait(false);
            var item = priceData?.FirstOrDefault();
            if (item?.Deals == null || item.Deals.Count == 0)
            {
                return null;
            }

            var offers = item.Deals
                .Where(d => d.Price is not null && d.Regular is not null)
                .Select(d => new StorePriceOffer
                {
                    StoreName = d.Shop.Name,
                    Price = d.Price.Amount,
                    RegularPrice = d.Regular.Amount,
                    DiscountPercent = d.Regular.Amount > 0 ? (int)((1 - (d.Price.Amount / d.Regular.Amount)) * 100) : 0,
                    DealUrl = d.Url
                })
                .OrderBy(o => o.Price)
                .ToList();

            return new PriceComparisonResult
            {
                AppId = appId,
                Title = lookup.Value.Title,
                Offers = offers
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to compare prices for AppId {AppId}.", appId);
            return null;
        }
    }

    /// <inheritdoc />
    public WishlistDealAlert? EvaluateAlert(uint appId, string gameTitle, ItadPriceData priceData, IReadOnlyList<ulong> notifiedUserIds)
    {
        if (priceData is null)
        {
            return null;
        }

        // Users requested free games and regular-priced games be excluded from deal alerts.
        if (priceData.CurrentPrice <= 0)
        {
            return null; // Free game
        }

        if (priceData.RegularPrice > 0 && priceData.CurrentPrice >= priceData.RegularPrice)
        {
            return null; // No actual discount
        }

        var resolvedTitle = string.IsNullOrWhiteSpace(priceData.Title) ? gameTitle : priceData.Title;
        var minDiscount = _options.DealMinDiscountPercent;
        var meetsThreshold = priceData.IsHistoricalLow || priceData.DiscountPercent >= minDiscount;

        if (!meetsThreshold)
        {
            return null;
        }

        return new WishlistDealAlert
        {
            AppId = appId,
            GameTitle = resolvedTitle,
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

    private static IEnumerable<List<T>> Chunk<T>(IReadOnlyList<T> source, int size)
    {
        for (var i = 0; i < source.Count; i += size)
        {
            yield return source.Skip(i).Take(size).ToList();
        }
    }

    private async Task<(string ItadId, string Title)?> LookupItadGameAsync(uint appId, CancellationToken cancellationToken)
    {
        var apiKey = _options.ItadApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogWarning("No IsThereAnyDeal API key configured. Skipping ITAD lookup.");
            return null;
        }

        try
        {
            var url = $"{ItadLookupEndpoint}?key={apiKey}&appid={appId}";
            using var response = await GetWithRetryAsync(url, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("ITAD lookup for Steam app {AppId} returned {StatusCode}.", appId, response.StatusCode);
                return null;
            }

            var responseJson = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var lookup = JsonSerializer.Deserialize<ItadLookupResponse>(responseJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (lookup?.Found != true || lookup?.Game?.Id == null)
            {
                _logger.LogWarning("ITAD lookup for app {AppId} returned no game ID. Raw response: {Raw}", appId, responseJson);
                return null;
            }

            return (lookup.Game.Id, lookup.Game.Title);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "ITAD lookup failed for Steam app {AppId}.", appId);
            return null;
        }
    }

    /// <summary>Performs a GET with retry.</summary>
    /// <param name="url">The URL.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The response.</returns>
    internal async Task<HttpResponseMessage> GetWithRetryAsync(string url, CancellationToken cancellationToken = default)
        => await ExecuteWithRetryAsync(() => _httpClient.GetAsync(url, cancellationToken), cancellationToken);


    /// <summary>Performs a POST with retry.</summary>
    /// <param name="url">The URL.</param>
    /// <param name="payload">The payload.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The response.</returns>
    internal async Task<HttpResponseMessage> PostWithRetryAsync(string url, object payload, CancellationToken cancellationToken = default)
        => await ExecuteWithRetryAsync(
            async () =>
            {
                using var content = JsonContent.Create(payload);
                return await _httpClient.PostAsync(url, content, cancellationToken);
            },
            cancellationToken);

    /// <summary>Executes a send factory with retry.</summary>
    /// <param name="sendFactory">The send factory.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The response.</returns>
    internal async Task<HttpResponseMessage> ExecuteWithRetryAsync(Func<Task<HttpResponseMessage>> sendFactory, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sendFactory);

        return await HttpRetry.ExecuteAsync(
            sendFactory,
            MaxAttempts,
            HttpRetry.IsTransientResponse,
            attempt => TimeSpan.FromSeconds(attempt),
            null,
            HttpRetry.IsTransientException,
            cancellationToken);
    }
}

// ITAD API response models

internal sealed class ItadLookupResponse
{
    [JsonPropertyName("found")]
    public bool Found { get; set; }

    [JsonPropertyName("game")]
    public ItadGameRef? Game { get; set; }
}

internal sealed class ItadGameRef
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;
}

internal sealed class ItadPriceResponse
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("deals")]
    public List<ItadDeal>? Deals { get; set; }

    [JsonPropertyName("historyLow")]
    public ItadHistoryLow? HistoryLow { get; set; }
}

internal sealed class ItadHistoryLow
{
    [JsonPropertyName("all")]
    public ItadPricePoint? All { get; set; }
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
