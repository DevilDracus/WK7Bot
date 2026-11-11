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
        // Shared wishlist links (store.steampowered.com/wishlist/id/{token}) grant read access
        // to private wishlists via a share token — use the store data endpoint for those.
        if (!string.IsNullOrWhiteSpace(wishlistUrl))
        {
            var shareMatch = System.Text.RegularExpressions.Regex.Match(wishlistUrl, @"wishlist/id/([^/?#]+)");
            if (shareMatch.Success)
            {
                var token = shareMatch.Groups[1].Value;
                // Fetch the store wishlist page (not wishlistdata/, which is deprecated and redirects to login).
                // The page embeds app IDs in SSR data when requested with a browser User-Agent.
                return $"https://store.steampowered.com/wishlist/id/{token}/";
            }

            var profileMatch = System.Text.RegularExpressions.Regex.Match(wishlistUrl, @"profiles/(\d+)");
            if (profileMatch.Success)
            {
                return $"https://api.steampowered.com/IWishlistService/GetWishlist/v1/?steamid={profileMatch.Groups[1].Value}";
            }
        }

        return $"https://api.steampowered.com/IWishlistService/GetWishlist/v1/?steamid={steamId}";
    }
    private const string ItadLookupEndpoint = "https://api.isthereanydeal.com/games/lookup/v1";
    private const string ItadPricesEndpoint = "https://api.isthereanydeal.com/games/prices/v3";
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

        if (!steamId.StartsWith("7656119", StringComparison.Ordinal) || steamId.Length != 17)
        {
            _logger.LogWarning("Steam ID {SteamId} does not look like a 64-bit Steam ID; wishlist fetch may fail.", steamId);
        }

        var url = ResolveWishlistUrl(steamId, wishlistUrl);

        try
        {
            using var response = url.Contains("store.steampowered.com", StringComparison.OrdinalIgnoreCase)
                ? await GetWithBrowserUserAgentAsync(url, cancellationToken)
                : await GetWithRetryAsync(url, cancellationToken);
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
                // The store wishlist page embeds app IDs in JavaScript (g_rgWishlistData / "appid":N).
                // Parse them so private wishlists accessible via shared links can still be used.
                var appIdMatches = System.Text.RegularExpressions.Regex.Matches(json, @"appid[^0-9]*(\d{5,8})");
                var appIds = appIdMatches.Cast<System.Text.RegularExpressions.Match>()
                    .Select(m => uint.TryParse(m.Groups[1].Value, out var id) ? id : 0u)
                    .Where(id => id > 0)
                    .Distinct()
                    .ToList();

                if (appIds.Count > 0)
                {
                    _logger.LogInformation("Parsed {Count} app ID(s) from Steam wishlist HTML.", appIds.Count);
                    return appIds.Select(id => new SteamWishlistItem { AppId = id }).ToList();
                }

                _logger.LogWarning(
                    "Steam wishlist for {SteamId} returned HTML instead of JSON (likely rate-limited, unavailable, or private without a valid share token).",
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

            // Fall back to the store wishlistdata/ format: { "730": { "name": "...", "appid": 730 } }
            // Used by shared wishlist links (store.steampowered.com/wishlist/id/{token}/wishlistdata/).
            var wishlist = JsonSerializer.Deserialize<SteamWishlistResponse>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            if (wishlist is { Count: > 0 })
            {
                return wishlist.Values.ToList();
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
            try
            {
                var url = $"{ItadLookupEndpoint}?key={apiKey}&appid={appId}";
                using var response = await GetWithRetryAsync(url, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("ITAD lookup for Steam app {AppId} returned {StatusCode}.", appId, response.StatusCode);
                    return (appId: appId, itadId: (string?)null, title: (string?)null);
                }

                var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
                var lookup = JsonSerializer.Deserialize<ItadLookupResponse>(responseJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (lookup?.Found != true || lookup?.Game?.Id == null)
                {
                    _logger.LogWarning("ITAD lookup for app {AppId} returned no game ID. Raw response: {Raw}", appId, responseJson);
                    return (appId: appId, itadId: (string?)null, title: (string?)null);
                }

                return (appId: appId, itadId: lookup.Game.Id, title: lookup.Game.Title);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || (ex is OperationCanceledException oce && !oce.CancellationToken.IsCancellationRequested))
            {
                _logger.LogDebug(ex, "ITAD lookup failed for Steam app {AppId}.", appId);
                return (appId: appId, itadId: (string?)null, title: (string?)null);
            }
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
            foreach (var chunk in Chunk(allItadIds, 200))
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
    public WishlistDealAlert? EvaluateAlert(uint appId, string gameTitle, ItadPriceData priceData, IReadOnlyList<ulong> notifiedUserIds)
    {
        if (priceData is null)
        {
            return null;
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

    internal async Task<HttpResponseMessage> GetWithRetryAsync(string url, CancellationToken cancellationToken = default)
        => await ExecuteWithRetryAsync(() => _httpClient.GetAsync(url, cancellationToken), cancellationToken);

    internal async Task<HttpResponseMessage> GetWithBrowserUserAgentAsync(string url, CancellationToken cancellationToken = default)
    {
        return await ExecuteWithRetryAsync(
            async () =>
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36");
                request.Headers.TryAddWithoutValidation("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
                request.Headers.TryAddWithoutValidation("Accept-Language", "en-US,en;q=0.9");
                return await _httpClient.SendAsync(request, cancellationToken);
            },
            cancellationToken);
    }

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
