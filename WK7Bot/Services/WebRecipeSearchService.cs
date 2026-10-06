namespace WK7Bot.Services;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using WK7Bot.Core.Exceptions;
using WK7Bot.Models;
using WK7Bot.Services.Interfaces;

/// <summary>
/// Looks up recipes on the public web by querying the keyless DuckDuckGo HTML endpoint (falling back to the Chefkoch
/// search when DuckDuckGo serves a bot challenge or fails) and parsing schema.org <c>application/ld+json</c>
/// <c>Recipe</c> blocks from the resulting pages. The originating page URL is preserved as
/// <see cref="RenalRecipeData.SourceUrl"/> so embeds can link back to the original source.
/// </summary>
public class WebRecipeSearchService : IRecipeSearchService
{
    private const string SearchEndpoint = "https://html.duckduckgo.com/html/";
    private const string ChefkochBaseUrl = "https://www.chefkoch.de";
    private const string SearchQuerySuffix = " Rezept";
    private const int MaxCandidates = 6;

    /// <summary>
    /// How many of the top-ranked candidates take part in the randomised start; the tail keeps its relevance order.
    /// </summary>
    private const int CandidateStartWindow = 4;

    /// <summary>
    /// Rolling memory of the most recently served source URLs, used to avoid handing out the same recipe over and over.
    /// </summary>
    private const string RecentRecipeCacheKey = "recipe:search:recently-served";

    private const int MaxRecentRecipes = 12;

    private static readonly TimeSpan RecentRecipeLifetime = TimeSpan.FromDays(2);

    private const string UserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36";

    /// <summary>
    /// URL fragments that identify search/category pages (they never expose a single parseable recipe).
    /// </summary>
    private static readonly string[] ListingMarkers =
    {
        "/rs/", "rezepte.html", "/kategorie", "/category", "/suche", "/search", "/listen/"
    };

    /// <summary>
    /// Hosts that never publish schema.org recipe data and would only waste fetch attempts.
    /// </summary>
    private static readonly string[] BlockedHostMarkers =
    {
        "youtube.com", "youtu.be", "pinterest.", "facebook.com", "instagram.com", "tiktok.com", "amazon.", "ebay."
    };

    private static readonly Regex ResultLinkRegex = new(
        @"<a\b(?=[^>]*\bclass\s*=\s*[""']result__a[""'])[^>]*?\bhref\s*=\s*[""'](?<href>[^""']+)[""']",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex JsonLdRegex = new(
        @"<script\b[^>]*type\s*=\s*[""']?application/ld\+json[""']?[^>]*>(?<json>.*?)</script>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex RedirectParamRegex = new(
        @"[?&]uddg=(?<url>[^&]+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex DurationRegex = new(
        @"^P(?:(?<days>\d+)D)?(?:T(?:(?<hours>\d+)H)?(?:(?<minutes>\d+)M)?(?:(?<seconds>\d+)S)?)?$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex LeadingIntegerRegex = new(
        @"\d+",
        RegexOptions.Compiled);

    /// <summary>
    /// Matches Chefkoch recipe page URLs (absolute or root-relative), always carrying a numeric recipe id.
    /// </summary>
    private static readonly Regex ChefkochRecipeUrlRegex = new(
        @"(?:https://www\.chefkoch\.de)?/rezepte/(?<id>\d+)/[^""'?#\s]+",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex MarkupRegex = new(
        "<[^>]+>",
        RegexOptions.Compiled);

    private readonly HttpClient _httpClient;
    private readonly ILogger<WebRecipeSearchService> _logger;
    private readonly IRandomSource _random;
    private readonly IMemoryCache? _recentRecipeCache;
    private readonly object _recentRecipeLock = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="WebRecipeSearchService"/> class.
    /// </summary>
    /// <param name="httpClient">The HTTP client used for search and recipe page requests.</param>
    /// <param name="logger">The logger instance.</param>
    /// <param name="randomSource">The randomness used to vary candidate selection; defaults to <see cref="SystemRandomSource"/>.</param>
    /// <param name="recentRecipeCache">
    /// Optional cache remembering the most recently served recipes; when <see langword="null"/>, candidates are only
    /// randomised and never filtered.
    /// </param>
    public WebRecipeSearchService(
        HttpClient httpClient,
        ILogger<WebRecipeSearchService> logger,
        IRandomSource? randomSource = null,
        IMemoryCache? recentRecipeCache = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _random = randomSource ?? new SystemRandomSource();
        _recentRecipeCache = recentRecipeCache;
    }

    /// <summary>
    /// Searches the web for a recipe matching the free text query and parses the first candidate page exposing
    /// structured recipe data. DuckDuckGo HTML search is the primary provider; when it is blocked (bot challenge,
    /// HTTP error) or yields no parseable recipe, the Chefkoch search is used as fallback.
    /// </summary>
    /// <param name="query">The free text search query.</param>
    /// <param name="cancellationToken">A token to monitor for task cancellation.</param>
    /// <returns>The parsed recipe with source metadata, or <see langword="null"/> when nothing could be parsed.</returns>
    /// <exception cref="RecipeSearchUnavailableException">Thrown when neither search provider responded.</exception>
    public async Task<RenalRecipeData?> SearchWebRecipeAsync(string query, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return null;
        }

        var searchHtml = await FetchAsync($"{SearchEndpoint}?q={Uri.EscapeDataString(query.Trim() + SearchQuerySuffix)}", cancellationToken)
            .ConfigureAwait(false);
        var searchAvailable = searchHtml != null && !IsChallengePage(searchHtml);

        if (searchAvailable)
        {
            var fromSearch = await TryParseFirstRecipeAsync(
                    SelectCandidates(ExtractCandidateUrls(searchHtml!)),
                    cancellationToken)
                .ConfigureAwait(false);
            if (fromSearch != null)
            {
                RememberRecipe(fromSearch.SourceUrl);
                return fromSearch;
            }
        }
        else
        {
            _logger.LogWarning("DuckDuckGo search did not respond for query '{Query}'. Falling back to Chefkoch.", query);
        }

        var listingHtml = await FetchAsync(BuildChefkochSearchUrl(query), cancellationToken).ConfigureAwait(false);
        if (listingHtml == null || IsChallengePage(listingHtml))
        {
            if (!searchAvailable)
            {
                throw new RecipeSearchUnavailableException($"No recipe search provider responded for query '{query}'.");
            }

            return null;
        }

        var fromListing = await TryParseFirstRecipeAsync(
                SelectCandidates(ExtractChefkochRecipeUrls(listingHtml)),
                cancellationToken)
            .ConfigureAwait(false);
        if (fromListing != null)
        {
            RememberRecipe(fromListing.SourceUrl);
            return fromListing;
        }

        _logger.LogWarning("No parseable recipe found for query '{Query}'.", query);
        return null;
    }

    /// <summary>
    /// Orders the candidate pages so repeated searches do not keep hitting the same recipe: candidates that were not
    /// served recently are tried first, and within each group the starting point is randomised with a bias towards the
    /// better-ranked results (so relevance is not thrown away entirely).
    /// </summary>
    private IReadOnlyList<string> SelectCandidates(IReadOnlyList<string> candidateUrls)
    {
        if (candidateUrls.Count <= 1)
        {
            return candidateUrls;
        }

        var unserved = new List<string>(candidateUrls.Count);
        var served = new List<string>(candidateUrls.Count);
        foreach (var candidateUrl in candidateUrls)
        {
            if (WasRecentlyServed(candidateUrl))
            {
                served.Add(candidateUrl);
            }
            else
            {
                unserved.Add(candidateUrl);
            }
        }

        if (served.Count == 0)
        {
            return OrderCandidates(unserved);
        }

        if (unserved.Count == 0)
        {
            return OrderCandidates(served);
        }

        var ordered = new List<string>(candidateUrls.Count);
        ordered.AddRange(OrderCandidates(unserved));
        ordered.AddRange(OrderCandidates(served));
        return ordered;
    }

    /// <summary>
    /// Picks a random start index inside the top-ranked candidates (weighting earlier results higher) and walks the
    /// list cyclically from there, so every top candidate has a chance to be tried first.
    /// </summary>
    private IReadOnlyList<string> OrderCandidates(IReadOnlyList<string> candidateUrls)
    {
        if (candidateUrls.Count <= 1)
        {
            return candidateUrls;
        }

        int window = Math.Min(CandidateStartWindow, candidateUrls.Count);
        int totalWeight = (window * (window + 1)) / 2;

        int roll = _random.Next(totalWeight);
        int start = 0;
        int cumulative = 0;
        for (int i = 0; i < window; i++)
        {
            cumulative += window - i;
            if (roll < cumulative)
            {
                start = i;
                break;
            }
        }

        var ordered = new List<string>(candidateUrls.Count);
        for (int i = 0; i < candidateUrls.Count; i++)
        {
            ordered.Add(candidateUrls[(start + i) % candidateUrls.Count]);
        }

        return ordered;
    }

    /// <summary>
    /// Determines whether the given source URL was served recently and should be avoided for now.
    /// </summary>
    private bool WasRecentlyServed(string sourceUrl)
    {
        if (_recentRecipeCache == null)
        {
            return false;
        }

        lock (_recentRecipeLock)
        {
            return _recentRecipeCache.TryGetValue(RecentRecipeCacheKey, out List<string>? recent)
                && recent != null
                && recent.Contains(sourceUrl, StringComparer.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Remembers a served source URL so subsequent searches prefer other recipes.
    /// </summary>
    private void RememberRecipe(string? sourceUrl)
    {
        if (_recentRecipeCache == null || string.IsNullOrWhiteSpace(sourceUrl))
        {
            return;
        }

        lock (_recentRecipeLock)
        {
            if (!_recentRecipeCache.TryGetValue(RecentRecipeCacheKey, out List<string>? recent) || recent == null)
            {
                recent = new List<string>(MaxRecentRecipes);
            }

            recent.RemoveAll(url => string.Equals(url, sourceUrl, StringComparison.OrdinalIgnoreCase));
            recent.Add(sourceUrl);
            if (recent.Count > MaxRecentRecipes)
            {
                recent.RemoveRange(0, recent.Count - MaxRecentRecipes);
            }

            _recentRecipeCache.Set(
                RecentRecipeCacheKey,
                recent,
                new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = RecentRecipeLifetime });
        }
    }

    /// <summary>
    /// Walks candidate recipe pages in order and returns the first one that exposes parseable recipe data.
    /// </summary>
    private async Task<RenalRecipeData?> TryParseFirstRecipeAsync(IReadOnlyList<string> candidateUrls, CancellationToken cancellationToken)
    {
        foreach (var candidateUrl in candidateUrls)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var pageHtml = await FetchAsync(candidateUrl, cancellationToken).ConfigureAwait(false);
            if (pageHtml == null || IsChallengePage(pageHtml))
            {
                continue;
            }

            var recipe = ParseRecipe(pageHtml, candidateUrl);
            if (recipe != null)
            {
                _logger.LogInformation("Parsed recipe '{Title}' from {SourceUrl}.", recipe.Title, candidateUrl);
                return recipe;
            }
        }

        return null;
    }

    /// <summary>
    /// Detects bot-challenge pages (e.g. DuckDuckGo's "select all squares containing a duck" interstitial).
    /// </summary>
    private static bool IsChallengePage(string html)
        => html.Contains("bots use DuckDuckGo", StringComparison.OrdinalIgnoreCase)
           || html.Contains("complete the following challenge", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Builds the Chefkoch recipe-search URL used as fallback provider.
    /// </summary>
    private static string BuildChefkochSearchUrl(string query)
    {
        var encoded = Uri.EscapeDataString(query.Trim()).Replace("%20", "+", StringComparison.Ordinal);
        return $"{ChefkochBaseUrl}/rs/s0/{encoded}/Rezepte.html";
    }

    /// <summary>
    /// Extracts the recipe page URLs published by a Chefkoch search result page (structured ItemList data and
    /// embedded links), de-duplicated by recipe id.
    /// </summary>
    private static IReadOnlyList<string> ExtractChefkochRecipeUrls(string listingHtml)
    {
        var urls = new List<string>();
        var seenIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (Match match in ChefkochRecipeUrlRegex.Matches(listingHtml))
        {
            var recipePath = match.Value;
            if (!seenIds.Add(match.Groups["id"].Value))
            {
                continue;
            }

            if (recipePath.StartsWith("/", StringComparison.Ordinal))
            {
                recipePath = ChefkochBaseUrl + recipePath;
            }

            urls.Add(recipePath);
            if (urls.Count >= MaxCandidates)
            {
                break;
            }
        }

        return urls;
    }

    /// <summary>
    /// Fetches a URL as text, returning <see langword="null"/> for transport failures or non-OK status codes.
    /// </summary>
    private async Task<string?> FetchAsync(string url, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
            request.Headers.TryAddWithoutValidation("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
            request.Headers.TryAddWithoutValidation("Accept-Language", "de-DE,de;q=0.9,en;q=0.8");

            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode != HttpStatusCode.OK)
            {
                _logger.LogWarning("Recipe lookup request to {Url} failed with status {StatusCode}.", url, response.StatusCode);
                return null;
            }

            return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
        {
            _logger.LogWarning(ex, "Recipe lookup request to {Url} failed.", url);
            return null;
        }
    }

    /// <summary>
    /// Extracts the result page URLs from a DuckDuckGo HTML results document, unwrapping redirect links.
    /// Real recipe pages are tried before search/category listings, while the original ranking is kept inside
    /// each group; sponsored links, blocked hosts and duplicates are dropped.
    /// </summary>
    private static IReadOnlyList<string> ExtractCandidateUrls(string searchHtml)
    {
        var urls = new List<string>();

        foreach (Match match in ResultLinkRegex.Matches(searchHtml))
        {
            var url = ResolveResultUrl(match.Groups["href"].Value);
            if (url == null)
            {
                continue;
            }

            if (urls.Any(existing => string.Equals(existing, url, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            urls.Add(url);
        }

        return urls
            .Select((url, index) => (url, index, isListing: LooksLikeListing(url)))
            .OrderBy(candidate => candidate.isListing)
            .ThenBy(candidate => candidate.index)
            .Take(MaxCandidates)
            .Select(candidate => candidate.url)
            .ToList();
    }

    /// <summary>
    /// Heuristically identifies search/category pages that never expose a single parseable recipe.
    /// </summary>
    private static bool LooksLikeListing(string url)
        => ListingMarkers.Any(marker => url.Contains(marker, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Determines whether a host is known to never publish schema.org recipe data.
    /// </summary>
    private static bool IsBlockedHost(string host)
        => BlockedHostMarkers.Any(marker => host.Contains(marker, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Decodes a DuckDuckGo redirect target (or plain absolute link) into a fetchable HTTP(S) URL.
    /// Returns <see langword="null"/> for DuckDuckGo ad/tracking links that carry no redirect target.
    /// </summary>
    private static string? ResolveResultUrl(string rawHref)
    {
        var href = WebUtility.HtmlDecode(rawHref ?? string.Empty).Trim();
        if (href.Length == 0)
        {
            return null;
        }

        if (href.StartsWith("//", StringComparison.Ordinal))
        {
            href = "https:" + href;
        }

        if (!Uri.TryCreate(href, UriKind.Absolute, out var uri))
        {
            return null;
        }

        if (uri.Host.EndsWith("duckduckgo.com", StringComparison.OrdinalIgnoreCase))
        {
            var redirect = RedirectParamRegex.Match(uri.ToString());
            if (!redirect.Success)
            {
                // Sponsored result (e.g. /y.js?...) without a redirect target: nothing to parse.
                return null;
            }

            var decoded = Uri.UnescapeDataString(redirect.Groups["url"].Value);
            if (!Uri.TryCreate(decoded, UriKind.Absolute, out var decodedUri) || decodedUri.Scheme is not ("http" or "https"))
            {
                return null;
            }

            uri = decodedUri;
        }

        if (uri.Scheme is not ("http" or "https") || IsBlockedHost(uri.Host))
        {
            return null;
        }

        return uri.ToString();
    }

    /// <summary>
    /// Parses the first schema.org <c>Recipe</c> node found in the JSON-LD blocks of a page.
    /// </summary>
    private static RenalRecipeData? ParseRecipe(string html, string sourceUrl)
    {
        foreach (Match block in JsonLdRegex.Matches(html))
        {
            var recipe = ParseJsonLdRecipe(block.Groups["json"].Value, sourceUrl);
            if (recipe != null)
            {
                return recipe;
            }
        }

        return null;
    }

    /// <summary>
    /// Maps a single JSON-LD document onto <see cref="RenalRecipeData"/>, returning <see langword="null"/> when the
    /// document contains no usable recipe node.
    /// </summary>
    private static RenalRecipeData? ParseJsonLdRecipe(string json, string sourceUrl)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            return null;
        }

        using (document)
        {
            foreach (var node in EnumerateRecipeNodes(document.RootElement))
            {
                var recipe = MapRecipeNode(node, sourceUrl);
                if (recipe != null)
                {
                    return recipe;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Enumerates the candidate objects of a JSON-LD document that may describe a recipe.
    /// </summary>
    private static IEnumerable<JsonElement> EnumerateRecipeNodes(JsonElement root)
    {
        switch (root.ValueKind)
        {
            case JsonValueKind.Array:
                return root.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.Object).ToList();

            case JsonValueKind.Object when root.TryGetProperty("@graph", out var graph) && graph.ValueKind == JsonValueKind.Array:
                return graph.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.Object).Append(root).ToList();

            case JsonValueKind.Object:
                return new[] { root };

            default:
                return Array.Empty<JsonElement>();
        }
    }

    /// <summary>
    /// Converts a JSON-LD object typed as <c>Recipe</c> into a recipe payload, or <see langword="null"/> when the
    /// object is not a recipe or lacks the ingredients and instructions a post needs.
    /// </summary>
    private static RenalRecipeData? MapRecipeNode(JsonElement node, string sourceUrl)
    {
        if (node.ValueKind != JsonValueKind.Object || !HasType(node, "Recipe"))
        {
            return null;
        }

        var ingredients = ReadStringList(node, "recipeIngredient");
        var instructions = ReadInstructions(node);
        var title = ReadString(node, "name");

        if (string.IsNullOrWhiteSpace(title) || ingredients.Count == 0 || instructions.Count == 0)
        {
            return null;
        }

        return new RenalRecipeData
        {
            Title = CleanText(title),
            Description = CleanText(ReadString(node, "description")),
            PrepTime = FormatDuration(ReadString(node, "prepTime")),
            CookTime = FormatDuration(ReadString(node, "cookTime")),
            Servings = ParseServings(ReadString(node, "recipeYield")),
            Ingredients = ingredients.Select(CleanText).Where(t => t.Length > 0).ToList(),
            Instructions = instructions.Select(CleanText).Where(t => t.Length > 0).ToList(),
            ImageUrl = ResolveImageUrl(node),
            SourceUrl = sourceUrl
        };
    }

    /// <summary>
    /// Determines whether a JSON-LD node declares the given schema.org type (string or array form, URL or prefix form).
    /// </summary>
    private static bool HasType(JsonElement node, string typeName)
    {
        if (!node.TryGetProperty("@type", out var typeProperty))
        {
            return false;
        }

        if (typeProperty.ValueKind == JsonValueKind.String)
        {
            return MatchesType(typeProperty.GetString(), typeName);
        }

        if (typeProperty.ValueKind == JsonValueKind.Array)
        {
            return typeProperty.EnumerateArray()
                .Any(entry => entry.ValueKind == JsonValueKind.String && MatchesType(entry.GetString(), typeName));
        }

        return false;
    }

    private static bool MatchesType(string? value, string typeName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalized = value.Trim();
        var separatorIndex = normalized.LastIndexOfAny(new[] { '/', ':' });
        if (separatorIndex >= 0)
        {
            normalized = normalized[(separatorIndex + 1)..];
        }

        return string.Equals(normalized, typeName, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Reads a scalar property (string or number), also accepting arrays such as schema.org
    /// <c>recipeYield</c> (<c>["4", "4 Portionen"]</c>) by taking the first usable entry.
    /// </summary>
    private static string ReadString(JsonElement node, string propertyName)
    {
        if (!node.TryGetProperty(propertyName, out var property))
        {
            return string.Empty;
        }

        return ReadScalar(property);
    }

    private static string ReadScalar(JsonElement property)
    {
        switch (property.ValueKind)
        {
            case JsonValueKind.String:
                return property.GetString() ?? string.Empty;

            case JsonValueKind.Number:
                return property.GetRawText();

            case JsonValueKind.Array:
                return property.EnumerateArray()
                    .Select(ReadScalar)
                    .FirstOrDefault(value => value.Length > 0) ?? string.Empty;

            default:
                return string.Empty;
        }
    }

    /// <summary>
    /// Reads a schema.org text property that may be a single string or an array of strings.
    /// </summary>
    private static List<string> ReadStringList(JsonElement node, string propertyName)
    {
        var values = new List<string>();
        if (!node.TryGetProperty(propertyName, out var property))
        {
            return values;
        }

        if (property.ValueKind == JsonValueKind.String)
        {
            var single = property.GetString();
            if (!string.IsNullOrWhiteSpace(single))
            {
                values.Add(single);
            }

            return values;
        }

        if (property.ValueKind != JsonValueKind.Array)
        {
            return values;
        }

        foreach (var entry in property.EnumerateArray())
        {
            if (entry.ValueKind == JsonValueKind.String)
            {
                var text = entry.GetString();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    values.Add(text);
                }
            }
            else if (entry.ValueKind == JsonValueKind.Object && entry.TryGetProperty("name", out var name)
                     && name.ValueKind == JsonValueKind.String)
            {
                var text = name.GetString();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    values.Add(text);
                }
            }
        }

        return values;
    }

    /// <summary>
    /// Flattens <c>recipeInstructions</c> (string, HowToStep array or nested HowToSection lists) into plain steps.
    /// </summary>
    private static List<string> ReadInstructions(JsonElement node)
    {
        var steps = new List<string>();
        if (!node.TryGetProperty("recipeInstructions", out var property))
        {
            return steps;
        }

        AppendInstructions(property, steps);
        return steps;
    }

    private static void AppendInstructions(JsonElement property, List<string> steps)
    {
        switch (property.ValueKind)
        {
            case JsonValueKind.String:
                var text = property.GetString();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    steps.Add(text);
                }

                break;

            case JsonValueKind.Object:
                if (property.TryGetProperty("itemListElement", out var nested))
                {
                    AppendInstructions(nested, steps);
                }
                else
                {
                    var stepText = ReadString(property, "text");
                    if (string.IsNullOrWhiteSpace(stepText))
                    {
                        stepText = ReadString(property, "name");
                    }

                    if (!string.IsNullOrWhiteSpace(stepText))
                    {
                        steps.Add(stepText);
                    }
                }

                break;

            case JsonValueKind.Array:
                foreach (var entry in property.EnumerateArray())
                {
                    AppendInstructions(entry, steps);
                }

                break;
        }
    }

    /// <summary>
    /// Resolves the first usable https hero image URL of a recipe node.
    /// </summary>
    private static string ResolveImageUrl(JsonElement node)
    {
        if (!node.TryGetProperty("image", out var image))
        {
            return string.Empty;
        }

        var candidates = image.ValueKind switch
        {
            JsonValueKind.String => new[] { image.GetString() ?? string.Empty },
            JsonValueKind.Object => new[] { ReadString(image, "url") },
            JsonValueKind.Array => image.EnumerateArray()
                .Select(entry => entry.ValueKind switch
                {
                    JsonValueKind.String => entry.GetString() ?? string.Empty,
                    JsonValueKind.Object => ReadString(entry, "url"),
                    _ => string.Empty
                })
                .ToArray(),
            _ => Array.Empty<string>()
        };

        return candidates.FirstOrDefault(url =>
            Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps) ?? string.Empty;
    }

    /// <summary>
    /// Formats an ISO 8601 duration (e.g. <c>PT1H30M</c>) as a German display string (e.g. <c>1 Std. 30 Min.</c>).
    /// </summary>
    private static string FormatDuration(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var match = DurationRegex.Match(value.Trim());
        if (!match.Success)
        {
            return value.Trim();
        }

        var parts = new List<string>();
        if (match.Groups["days"].Success)
        {
            parts.Add($"{match.Groups["days"].Value} Tg.");
        }

        if (match.Groups["hours"].Success)
        {
            parts.Add($"{match.Groups["hours"].Value} Std.");
        }

        if (match.Groups["minutes"].Success)
        {
            parts.Add($"{match.Groups["minutes"].Value} Min.");
        }

        if (match.Groups["seconds"].Success)
        {
            parts.Add($"{match.Groups["seconds"].Value} Sek.");
        }

        return string.Join(" ", parts);
    }

    /// <summary>
    /// Extracts the first integer of a schema.org yield string such as <c>4 Portionen</c>.
    /// </summary>
    private static int ParseServings(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return 0;
        }

        var match = LeadingIntegerRegex.Match(value);
        return match.Success && int.TryParse(match.Value, out var servings) ? servings : 0;
    }

    /// <summary>
    /// Strips markup, decodes HTML entities and collapses whitespace in scraped text.
    /// </summary>
    private static string CleanText(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var decoded = WebUtility.HtmlDecode(value);
        var withoutMarkup = MarkupRegex.Replace(decoded, " ");
        return string.Join(" ", withoutMarkup.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }
}
