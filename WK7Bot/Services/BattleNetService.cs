using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WK7Bot.Models;
using WK7Bot.Options;
using WK7Bot.Services.Interfaces;

namespace WK7Bot.Services;

/// <summary>
/// Communicates with the Battle.net OAuth 2.0 endpoints and Blizzard game APIs to fetch account
/// identity, World of Warcraft characters, and Diablo heroes for linked accounts.
/// </summary>
public partial class BattleNetService : IBattleNetService
{
    private const int MaxAttempts = 3;
    private const int MaxWowCharacters = 5;

    private readonly HttpClient _httpClient;
    private readonly IMemoryCache _cache;
    private readonly Wk7BotOptions _options;
    private readonly ILogger<BattleNetService> _logger;
    private readonly SemaphoreSlim _apiThrottle = new(2, 2);

    /// <summary>
    /// Serializes OAuth token exchanges across all service instances, so concurrent callers share one
    /// refresh grant instead of racing the token endpoint (a second refresh grant could invalidate the
    /// first one). Static because the access-token cache it protects is shared application-wide.
    /// </summary>
    private static readonly SemaphoreSlim TokenRefreshGate = new(1, 1);

    /// <summary>
    /// Gets the base delay applied between retry attempts for transient Battle.net API failures. Virtual for testability.
    /// </summary>
    protected virtual TimeSpan RetryDelay => TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// Initializes a new instance of the <see cref="BattleNetService"/> class.
    /// </summary>
    /// <param name="httpClient">The HTTP client instance configured for Battle.net requests.</param>
    /// <param name="cache">The memory cache instance used to cache OAuth access tokens.</param>
    /// <param name="options">The strongly-typed application options instance containing credentials and mappings.</param>
    /// <param name="logger">The logging service instance for operational diagnostics.</param>
    /// <exception cref="ArgumentNullException">Thrown when any required dependency is null.</exception>
    public BattleNetService(
        HttpClient httpClient,
        IMemoryCache cache,
        IOptions<Wk7BotOptions> options,
        ILogger<BattleNetService> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    }

    /// <summary>
    /// Resolves the configured Battle.net mapping for a specified Discord user snowflake ID.
    /// </summary>
    /// <param name="discordUserId">The target Discord user ID.</param>
    /// <returns>The matching mapping, or null if no mapping exists.</returns>
    public DiscordBattleNetMappingOptions? GetMappingForDiscordUser(string discordUserId)
    {
        return _options.DiscordBattleNetMappings
            .FirstOrDefault(m => string.Equals(m.DiscordUserId, discordUserId, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Exchanges the refresh token for a cached access token, reads the account identity, and enriches
    /// it with the World of Warcraft characters and Diablo heroes of that account.
    /// </summary>
    /// <param name="refreshToken">The OAuth refresh token of the mapped Battle.net account.</param>
    /// <param name="cancellationToken">A cancellation token to monitor for task cancellation.</param>
    /// <returns>A populated <see cref="BattleNetUserData"/> model, or null if retrieval fails.</returns>
    public async Task<BattleNetUserData?> GetBattleNetUserDataAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(refreshToken);

        if (string.IsNullOrWhiteSpace(_options.BattleNetClientId) || string.IsNullOrWhiteSpace(_options.BattleNetClientSecret))
        {
            _logger.LogWarning("Battle.net client ID or client secret is missing in configuration. Skipping Battle.net data retrieval.");
            return null;
        }

        try
        {
            var mapping = _options.DiscordBattleNetMappings
                .FirstOrDefault(m => string.Equals(m.RefreshToken, refreshToken, StringComparison.Ordinal));
            var region = ResolveRegion(mapping?.Region);

            var tokenResult = await GetAccessTokenAsync(refreshToken, region, cancellationToken);
            if (tokenResult.Token is null)
            {
                return null;
            }

            var accessToken = tokenResult.Token;
            var (userInfo, userInfoStatus) = await FetchUserInfoAsync(accessToken, region, cancellationToken);

            if (userInfo is null
                && userInfoStatus == HttpStatusCode.Unauthorized
                && tokenResult.FromCache)
            {
                _logger.LogInformation(
                    "Battle.net access token was rejected with 401; refreshing it and retrying the userinfo request for region {Region}.",
                    region);

                var refreshed = await GetAccessTokenAsync(refreshToken, region, cancellationToken, forceRefresh: true);
                if (refreshed.Token is null)
                {
                    return null;
                }

                accessToken = refreshed.Token;
                (userInfo, _) = await FetchUserInfoAsync(accessToken, region, cancellationToken);
            }

            var battleTag = userInfo?.BattleTag;
            if (string.IsNullOrWhiteSpace(battleTag))
            {
                battleTag = mapping?.BattleTag?.Trim();
            }

            if (string.IsNullOrWhiteSpace(battleTag))
            {
                _logger.LogWarning(
                    "Battle.net userinfo returned no BattleTag and no fallback battle_tag is configured; skipping Battle.net data retrieval.");
                return null;
            }

            var userData = new BattleNetUserData
            {
                BattleNetId = userInfo?.Id?.ToString() ?? string.Empty,
                BattleTag = battleTag,
                Region = region
            };

            await EnrichWowCharactersAsync(userData, accessToken, region, cancellationToken);
            await EnrichDiabloHeroesAsync(userData, accessToken, region, cancellationToken);

            return userData;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Error occurred while fetching Battle.net data.");
            return null;
        }
    }

    /// <summary>
    /// Resolves the API region for a request: the per-account override when configured, otherwise the
    /// global <c>battlenet_region</c> option, otherwise the eu default.
    /// </summary>
    /// <param name="mappingRegion">The optional region of the mapped account.</param>
    /// <returns>The lower-case region identifier (us, eu, kr, tw).</returns>
    private string ResolveRegion(string? mappingRegion)
    {
        if (!string.IsNullOrWhiteSpace(mappingRegion))
        {
            return mappingRegion.Trim().ToLowerInvariant();
        }

        return string.IsNullOrWhiteSpace(_options.BattleNetRegion)
            ? "eu"
            : _options.BattleNetRegion.Trim().ToLowerInvariant();
    }

    /// <summary>
    /// Exchanges the configured refresh token for an access token, reusing a cached token until it is
    /// close to its expiry so rapid presence updates do not hammer the token endpoint.
    /// </summary>
    /// <param name="refreshToken">The OAuth refresh token of the mapped Battle.net account.</param>
    /// <param name="region">The API region whose OAuth host is used.</param>
    /// <param name="cancellationToken">A cancellation token to monitor for task cancellation.</param>
    /// <param name="forceRefresh">When true, a cached token is discarded before requesting a new one.</param>
    /// <returns>The access token (or null on failure) together with a flag telling whether it came from the cache.</returns>
    private async Task<(string? Token, bool FromCache)> GetAccessTokenAsync(
        string refreshToken,
        string region,
        CancellationToken cancellationToken,
        bool forceRefresh = false)
    {
        var cacheKey = BuildTokenCacheKey(refreshToken);

        if (forceRefresh)
        {
            _cache.Remove(cacheKey);
        }

        if (_cache.TryGetValue<string>(cacheKey, out var cached) && !string.IsNullOrEmpty(cached))
        {
            return (cached, true);
        }

        // Single-flight: concurrent lookups for the same account must not stampede the token endpoint.
        // A second refresh grant could race or invalidate the first one, so all callers funnel through
        // the shared gate and re-check the cache after acquiring it.
        await TokenRefreshGate.WaitAsync(cancellationToken);
        try
        {
            if (_cache.TryGetValue<string>(cacheKey, out var refreshed) && !string.IsNullOrEmpty(refreshed))
            {
                return (refreshed, true);
            }

            string clientId = _options.BattleNetClientId ?? string.Empty;
            string clientSecret = _options.BattleNetClientSecret ?? string.Empty;
            string tokenUrl = $"https://{region}.battle.net/oauth/token";

            using var response = await SendBnetApiWithRetryAsync(async () =>
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, tokenUrl)
                {
                    Content = new FormUrlEncodedContent(new Dictionary<string, string>
                    {
                        ["grant_type"] = "refresh_token",
                        ["refresh_token"] = refreshToken
                    })
                };
                request.Headers.Authorization = new AuthenticationHeaderValue(
                    "Basic",
                    Convert.ToBase64String(Encoding.UTF8.GetBytes($"{clientId}:{clientSecret}")));

                return await _httpClient.SendAsync(request, cancellationToken);
            }, "RefreshAccessToken", cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Battle.net OAuth token endpoint returned status code {StatusCode} for region {Region}. "
                    + "Check the client ID/client secret and that the refresh token was not revoked.",
                    response.StatusCode,
                    region);
                return (null, false);
            }

            var payload = await response.Content.ReadFromJsonAsync<BattleNetTokenResponse>(cancellationToken: cancellationToken);
            if (string.IsNullOrWhiteSpace(payload?.AccessToken))
            {
                _logger.LogWarning("Battle.net OAuth token endpoint returned no access token for region {Region}.", region);
                return (null, false);
            }

            int expiresIn = payload.ExpiresIn > 0 ? payload.ExpiresIn : 3600;
            var cacheTtl = TimeSpan.FromSeconds(Math.Max(60, expiresIn - 300));
            _cache.Set(cacheKey, payload.AccessToken!, cacheTtl);

            _logger.LogDebug("Refreshed Battle.net access token for region {Region} (valid for {Ttl}).", region, cacheTtl);

            return (payload.AccessToken, false);
        }
        finally
        {
            TokenRefreshGate.Release();
        }
    }

    /// <summary>
    /// Reads the account ID and BattleTag of the authorized account from the OAuth userinfo endpoint.
    /// </summary>
    /// <param name="accessToken">The user access token.</param>
    /// <param name="region">The API region whose OAuth host is used.</param>
    /// <param name="cancellationToken">A cancellation token to monitor for cancellation requests.</param>
    /// <returns>The parsed user info together with the HTTP status of the attempt (the status is meaningful when the info is null).</returns>
    private async Task<(BattleNetUserInfoResponse? Info, HttpStatusCode StatusCode)> FetchUserInfoAsync(string accessToken, string region, CancellationToken cancellationToken)
    {
        string url = $"https://{region}.battle.net/oauth/userinfo";

        using var response = await SendBnetApiWithRetryAsync(() => SendAuthorizedAsync(url, accessToken, cancellationToken), "GetUserInfo", cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "Battle.net OAuth userinfo returned status code {StatusCode} for region {Region}.",
                response.StatusCode,
                region);
            return (null, response.StatusCode);
        }

        return (await response.Content.ReadFromJsonAsync<BattleNetUserInfoResponse>(cancellationToken: cancellationToken), HttpStatusCode.OK);
    }

    /// <summary>
    /// Reads the account's World of Warcraft characters and enriches each of them (up to
    /// <see cref="MaxWowCharacters"/>) with level, class, faction, item levels, guild, last login and avatar.
    /// </summary>
    /// <param name="userData">The user data model to populate.</param>
    /// <param name="accessToken">The user access token.</param>
    /// <param name="region">The API region (also the profile namespace).</param>
    /// <param name="cancellationToken">A cancellation token to monitor for task cancellation.</param>
    /// <returns>A task tracking the asynchronous operation.</returns>
    private async Task EnrichWowCharactersAsync(BattleNetUserData userData, string accessToken, string region, CancellationToken cancellationToken)
    {
        string url = $"https://{region}.api.blizzard.com/profile/user/wow?namespace=profile-{region}&locale={Locale}";

        using var response = await SendBnetApiWithRetryAsync(() => SendAuthorizedAsync(url, accessToken, cancellationToken), "GetAccountProfileSummary", cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            if (response.StatusCode == HttpStatusCode.Forbidden)
            {
                _logger.LogWarning(
                    "Battle.net API returned 403 Forbidden for the WoW account profile (BattleTag: {BattleTag}). "
                    + "The refresh token was likely issued without the 'wow.profile' scope; re-authorize with that scope granted to show WoW characters.",
                    userData.BattleTag);
            }
            else
            {
                _logger.LogWarning(
                    "Battle.net API returned status code {StatusCode} for the WoW account profile (BattleTag: {BattleTag}); skipping WoW characters.",
                    response.StatusCode,
                    userData.BattleTag);
            }

            return;
        }

        var payload = await response.Content.ReadFromJsonAsync<BattleNetWowAccountProfileResponse>(cancellationToken: cancellationToken);

        var characterKeys = (payload?.WowAccounts ?? new List<BattleNetWowAccount>())
            .SelectMany(account => account.Characters ?? new List<BattleNetWowCharacterKeyEntry>())
            .Select(entry => TryParseCharacterKey(entry.Key?.Href))
            .Where(key => key.HasValue)
            .Select(key => key!.Value)
            .Distinct()
            .Take(MaxWowCharacters)
            .ToList();

        if (characterKeys.Count == 0)
        {
            _logger.LogInformation("Battle.net account {BattleTag} has no World of Warcraft characters.", userData.BattleTag);
            return;
        }

        // One failing character must not discard the whole account payload (identity + all other
        // characters + heroes), so every fetch carries its own error boundary.
        var fetchTasks = characterKeys.Select(key => FetchWowCharacterSafelyAsync(key.RealmSlug, key.Name, accessToken, region, cancellationToken));
        var characters = (await Task.WhenAll(fetchTasks)).Where(c => c is not null).Cast<BattleNetWowCharacter>().ToList();

        foreach (var character in characters.OrderByDescending(c => c.Level).ThenByDescending(c => c.LastLoginTimestamp))
        {
            userData.WowCharacters.Add(character);
        }

        var mainAvatar = userData.WowCharacters
            .Select(c => c.AvatarUrl)
            .FirstOrDefault(candidate => !string.IsNullOrWhiteSpace(candidate));

        if (mainAvatar is not null)
        {
            userData.AvatarUrl = mainAvatar;
        }
    }

    /// <summary>
    /// Retrieves a single World of Warcraft character profile and its character media (avatar art),
    /// converting transport and parsing failures into a null result so the remaining characters of
    /// the same account survive.
    /// </summary>
    /// <param name="realmSlug">The realm slug of the character.</param>
    /// <param name="characterName">The name of the character.</param>
    /// <param name="accessToken">The user access token.</param>
    /// <param name="region">The API region (also the profile namespace).</param>
    /// <param name="cancellationToken">Cancellation token to monitor for cancellation requests.</param>
    /// <returns>The parsed character, or null when the profile cannot be read.</returns>
    private async Task<BattleNetWowCharacter?> FetchWowCharacterSafelyAsync(
        string realmSlug,
        string characterName,
        string accessToken,
        string region,
        CancellationToken cancellationToken)
    {
        try
        {
            return await FetchWowCharacterAsync(realmSlug, characterName, accessToken, region, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Could not read WoW character {Realm}/{Character}; skipping it.",
                realmSlug,
                characterName);
            return null;
        }
    }

    /// <summary>
    /// Retrieves a single World of Warcraft character profile and its character media (avatar art).
    /// </summary>
    /// <param name="realmSlug">The realm slug of the character.</param>
    /// <param name="characterName">The character name.</param>
    /// <param name="accessToken">The user access token.</param>
    /// <param name="region">The API region (also the profile namespace).</param>
    /// <param name="cancellationToken">A cancellation token to monitor for task cancellation.</param>
    /// <returns>The parsed character, or null when the profile cannot be read.</returns>
    private async Task<BattleNetWowCharacter?> FetchWowCharacterAsync(
        string realmSlug,
        string characterName,
        string accessToken,
        string region,
        CancellationToken cancellationToken)
    {
        string characterUrl =
            $"https://{region}.api.blizzard.com/profile/wow/character/{realmSlug}/{Uri.EscapeDataString(characterName)}"
            + $"?namespace=profile-{region}&locale={Locale}";

        using var response = await SendBnetApiWithRetryAsync(() => SendAuthorizedAsync(characterUrl, accessToken, cancellationToken), "GetCharacterProfile", cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "Battle.net API returned status code {StatusCode} for WoW character {Realm}/{Character}.",
                response.StatusCode,
                realmSlug,
                characterName);
            return null;
        }

        var summary = await response.Content.ReadFromJsonAsync<BattleNetWowCharacterSummaryResponse>(cancellationToken: cancellationToken);
        if (summary is null)
        {
            return null;
        }

        return new BattleNetWowCharacter
        {
            Name = string.IsNullOrWhiteSpace(summary.Name) ? characterName : summary.Name!,
            RealmSlug = string.IsNullOrWhiteSpace(summary.Realm?.Slug) ? realmSlug : summary.Realm!.Slug!,
            Realm = summary.Realm?.Name ?? string.Empty,
            CharacterClass = summary.CharacterClass?.Name ?? string.Empty,
            Faction = summary.Faction?.Name ?? string.Empty,
            Level = summary.Level,
            AverageItemLevel = summary.AverageItemLevel,
            EquippedItemLevel = summary.EquippedItemLevel,
            GuildName = summary.Guild?.Name,
            LastLoginTimestamp = NormalizeUnixTimestamp(summary.LastLoginTimestamp),
            AvatarUrl = await FetchWowCharacterAvatarAsync(realmSlug, characterName, accessToken, region, cancellationToken)
        };
    }

    /// <summary>
    /// Retrieves the character media assets and picks the avatar image (falling back to bust or profile art).
    /// </summary>
    /// <param name="realmSlug">The realm slug of the character.</param>
    /// <param name="characterName">The character name.</param>
    /// <param name="accessToken">The user access token.</param>
    /// <param name="region">The API region (also the profile namespace).</param>
    /// <param name="cancellationToken">A cancellation token to monitor for task cancellation.</param>
    /// <returns>The avatar image URL, or null when media cannot be read.</returns>
    private async Task<string?> FetchWowCharacterAvatarAsync(
        string realmSlug,
        string characterName,
        string accessToken,
        string region,
        CancellationToken cancellationToken)
    {
        string mediaUrl =
            $"https://{region}.api.blizzard.com/profile/wow/character/{realmSlug}/{Uri.EscapeDataString(characterName)}/character-media"
            + $"?namespace=profile-{region}&locale={Locale}";

        using var response = await SendBnetApiWithRetryAsync(() => SendAuthorizedAsync(mediaUrl, accessToken, cancellationToken), "GetCharacterMedia", cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogDebug(
                "Battle.net API returned status code {StatusCode} for the WoW character media of {Realm}/{Character}.",
                response.StatusCode,
                realmSlug,
                characterName);
            return null;
        }

        var payload = await response.Content.ReadFromJsonAsync<BattleNetWowCharacterMediaResponse>(cancellationToken: cancellationToken);
        var assets = payload?.Assets;
        if (assets is null || assets.Count == 0)
        {
            return null;
        }

        foreach (var preferredKey in new[] { "avatar", "bust", "profile" })
        {
            var match = assets.FirstOrDefault(a =>
                string.Equals(a.Key, preferredKey, StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(a.Value));

            if (match?.Value is not null)
            {
                return match.Value;
            }
        }

        return null;
    }

    /// <summary>
    /// Reads the Diablo III heroes of the account. Blizzard does not publish a Diablo IV profile API,
    /// so this section stays empty for D4-only accounts.
    /// </summary>
    /// <param name="userData">The user data model to populate.</param>
    /// <param name="accessToken">The user access token.</param>
    /// <param name="region">The API region.</param>
    /// <param name="cancellationToken">A cancellation token to monitor for task cancellation.</param>
    /// <returns>A task tracking the asynchronous operation.</returns>
    private async Task EnrichDiabloHeroesAsync(BattleNetUserData userData, string accessToken, string region, CancellationToken cancellationToken)
    {
        string url = $"https://{region}.api.blizzard.com/d3/profile/{Uri.EscapeDataString(userData.BattleTag)}/?locale={Locale}";

        using var response = await SendBnetApiWithRetryAsync(() => SendAuthorizedAsync(url, accessToken, cancellationToken), "GetDiabloProfile", cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            if (response.StatusCode == HttpStatusCode.Forbidden)
            {
                _logger.LogWarning(
                    "Battle.net API returned 403 Forbidden for the Diablo III profile of {BattleTag}. "
                    + "The refresh token was likely issued without the 'd3.profile' scope; re-authorize with that scope granted to show Diablo heroes.",
                    userData.BattleTag);
            }
            else
            {
                _logger.LogWarning(
                    "Battle.net API returned status code {StatusCode} for the Diablo III profile of {BattleTag}; skipping Diablo heroes.",
                    response.StatusCode,
                    userData.BattleTag);
            }

            return;
        }

        var payload = await response.Content.ReadFromJsonAsync<BattleNetDiabloProfileResponse>(cancellationToken: cancellationToken);
        var heroes = payload?.Heroes;
        if (heroes is null || heroes.Count == 0)
        {
            return;
        }

        userData.DiabloHeroes = heroes
            .Select(hero => new BattleNetDiabloHero
            {
                Name = hero.Name ?? string.Empty,
                HeroClass = hero.Class ?? string.Empty,
                Level = hero.Level ?? 0,
                ParagonLevel = hero.ParagonLevel ?? 0,
                Seasonal = hero.Seasonal,
                Hardcore = hero.Hardcore,
                Dead = hero.Dead
            })
            .ToList();
    }

    /// <summary>
    /// Sends an authorized GET request under the shared API concurrency throttle.
    /// </summary>
    /// <param name="url">The fully qualified request URL.</param>
    /// <param name="accessToken">The bearer token attached to the request.</param>
    /// <param name="cancellationToken">A cancellation token to monitor for cancellation requests.</param>
    /// <returns>The received <see cref="HttpResponseMessage"/>.</returns>
    private Task<HttpResponseMessage> SendAuthorizedAsync(string url, string accessToken, CancellationToken cancellationToken)
    {
        return SendThrottledAsync(async () =>
        {
            // Dispose the request right after the send; the caller only owns the response.
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            return await _httpClient.SendAsync(request, cancellationToken);
        }, cancellationToken);
    }

    /// <summary>
    /// Executes a Battle.net API request while retrying transient failures (429/5xx) with linear backoff.
    /// </summary>
    /// <param name="sendFactory">Factory creating a fresh request per attempt (requests cannot be reused).</param>
    /// <param name="operationName">The logical Battle.net operation name used in log messages.</param>
    /// <param name="cancellationToken">A cancellation token to monitor for cancellation requests.</param>
    /// <returns>The final <see cref="HttpResponseMessage"/>; the caller owns and must dispose it.</returns>
    private async Task<HttpResponseMessage> SendBnetApiWithRetryAsync(
        Func<Task<HttpResponseMessage>> sendFactory,
        string operationName,
        CancellationToken cancellationToken)
    {
        HttpResponseMessage response = await sendFactory();

        for (int attempt = 1; attempt < MaxAttempts && IsTransientFailure(response); attempt++)
        {
            response.Dispose();

            var delay = TimeSpan.FromMilliseconds(RetryDelay.TotalMilliseconds * attempt);
            _logger.LogWarning(
                "Battle.net API {Operation} returned a transient failure (attempt {Attempt}/{MaxAttempts}); retrying after {Delay}.",
                operationName,
                attempt,
                MaxAttempts,
                delay);

            await Task.Delay(delay, cancellationToken);
            response = await sendFactory();
        }

        if (IsTransientFailure(response))
        {
            _logger.LogWarning(
                "Battle.net API {Operation} still failing with status code {StatusCode} after {MaxAttempts} attempts.",
                operationName,
                response.StatusCode,
                MaxAttempts);
        }

        return response;
    }

    /// <summary>
    /// Performs a request under the shared API concurrency throttle.
    /// </summary>
    /// <param name="sendFactory">Factory creating the request to send.</param>
    /// <param name="cancellationToken">A cancellation token to monitor for cancellation requests.</param>
    /// <returns>The received <see cref="HttpResponseMessage"/>.</returns>
    private async Task<HttpResponseMessage> SendThrottledAsync(Func<Task<HttpResponseMessage>> sendFactory, CancellationToken cancellationToken)
    {
        await _apiThrottle.WaitAsync(cancellationToken);
        try
        {
            return await sendFactory();
        }
        finally
        {
            _apiThrottle.Release();
        }
    }

    /// <summary>
    /// Determines whether a response represents a transient Battle.net API failure eligible for retry (429 or 5xx).
    /// </summary>
    /// <param name="response">The response to evaluate.</param>
    /// <returns>True when the request should be retried; otherwise false.</returns>
    private static bool IsTransientFailure(HttpResponseMessage response)
    {
        return response.StatusCode == HttpStatusCode.TooManyRequests
            || (int)response.StatusCode >= 500;
    }

    /// <summary>
    /// Parses a character profile URL into its realm slug and character name.
    /// </summary>
    /// <param name="href">The profile URL returned by the account profile endpoint.</param>
    /// <returns>The realm slug and name, or null when the URL does not match the expected shape.</returns>
    private static (string RealmSlug, string Name)? TryParseCharacterKey(string? href)
    {
        if (string.IsNullOrWhiteSpace(href))
        {
            return null;
        }

        var match = CharacterProfileUrlRegex().Match(href);
        if (!match.Success)
        {
            return null;
        }

        return (match.Groups[1].Value, Uri.UnescapeDataString(match.Groups[2].Value));
    }

    /// <summary>
    /// Converts a Blizzard unix timestamp into a UTC offset, tolerating both millisecond and second precision.
    /// </summary>
    /// <param name="unixTimestamp">The raw timestamp value (0 or negative when unknown).</param>
    /// <returns>The parsed timestamp, or null when no value was provided.</returns>
    private static DateTimeOffset? NormalizeUnixTimestamp(long unixTimestamp)
    {
        if (unixTimestamp <= 0)
        {
            return null;
        }

        return unixTimestamp > 100_000_000_000
            ? DateTimeOffset.FromUnixTimeMilliseconds(unixTimestamp)
            : DateTimeOffset.FromUnixTimeSeconds(unixTimestamp);
    }

    /// <summary>
    /// Builds the memory cache key of an access token from a hash of the refresh token, so the token
    /// itself never shows up in cache keys or logs.
    /// </summary>
    /// <param name="refreshToken">The OAuth refresh token.</param>
    /// <returns>The cache key.</returns>
    private static string BuildTokenCacheKey(string refreshToken)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(refreshToken));
        return $"battlenet_access_token_v1_{Convert.ToHexString(hash)[..16]}";
    }

    /// <summary>
    /// Gets the Battle.net API locale used to localize class, faction and realm names.
    /// </summary>
    private string Locale => string.IsNullOrWhiteSpace(_options.BattleNetLocale)
        ? "de_DE"
        : _options.BattleNetLocale.Trim();

    /// <summary>
    /// Matches character profile URLs such as <c>.../profile/wow/character/&lt;realm&gt;/&lt;name&gt;</c>.
    /// </summary>
    /// <returns>The compiled regular expression.</returns>
    [GeneratedRegex(@"/profile/wow/character/([^/?]+)/([^/?]+)", RegexOptions.CultureInvariant)]
    private static partial Regex CharacterProfileUrlRegex();
}

#region JSON DTO Models

/// <summary>
/// Root container model for the Battle.net OAuth token response.
/// </summary>
internal class BattleNetTokenResponse
{
    /// <summary>
    /// Gets or sets the issued access token.
    /// </summary>
    [JsonPropertyName("access_token")]
    public string? AccessToken { get; set; }

    /// <summary>
    /// Gets or sets the token lifetime in seconds.
    /// </summary>
    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; set; }

    /// <summary>
    /// Gets or sets the space-separated scopes granted to the token.
    /// </summary>
    [JsonPropertyName("scope")]
    public string? Scope { get; set; }
}

/// <summary>
/// Root container model for the Battle.net OAuth userinfo response.
/// </summary>
internal class BattleNetUserInfoResponse
{
    /// <summary>
    /// Gets or sets the numeric account identifier, or null when the endpoint did not report one.
    /// </summary>
    [JsonPropertyName("id")]
    public long? Id { get; set; }

    /// <summary>
    /// Gets or sets the account BattleTag (e.g. "Arthas#21234").
    /// </summary>
    [JsonPropertyName("battletag")]
    public string? BattleTag { get; set; }

    /// <summary>
    /// Gets or sets the token subject claim.
    /// </summary>
    [JsonPropertyName("sub")]
    public string? Subject { get; set; }
}

/// <summary>
/// Root container model for the WoW account profile summary.
/// </summary>
internal class BattleNetWowAccountProfileResponse
{
    /// <summary>
    /// Gets or sets the World of Warcraft accounts linked to the Battle.net account.
    /// </summary>
    [JsonPropertyName("wow_accounts")]
    public List<BattleNetWowAccount>? WowAccounts { get; set; }
}

/// <summary>
/// A single WoW account entry holding its characters.
/// </summary>
internal class BattleNetWowAccount
{
    /// <summary>
    /// Gets or sets the character key entries of this WoW account.
    /// </summary>
    [JsonPropertyName("characters")]
    public List<BattleNetWowCharacterKeyEntry>? Characters { get; set; }
}

/// <summary>
/// A character reference as returned by the account profile summary.
/// </summary>
internal class BattleNetWowCharacterKeyEntry
{
    /// <summary>
    /// Gets or sets the link to the full character profile.
    /// </summary>
    [JsonPropertyName("key")]
    public BattleNetHrefKey? Key { get; set; }
}

/// <summary>
/// A hyperlink wrapper object.
/// </summary>
internal class BattleNetHrefKey
{
    /// <summary>
    /// Gets or sets the target URL.
    /// </summary>
    [JsonPropertyName("href")]
    public string? Href { get; set; }
}

/// <summary>
/// Root container model for the WoW character profile summary.
/// </summary>
internal class BattleNetWowCharacterSummaryResponse
{
    /// <summary>
    /// Gets or sets the character name.
    /// </summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>
    /// Gets or sets the realm reference of the character.
    /// </summary>
    [JsonPropertyName("realm")]
    public BattleNetWowRealmRef? Realm { get; set; }

    /// <summary>
    /// Gets or sets the character level.
    /// </summary>
    [JsonPropertyName("level")]
    public int Level { get; set; }

    /// <summary>
    /// Gets or sets the character class reference.
    /// </summary>
    [JsonPropertyName("character_class")]
    public BattleNetWowNameRef? CharacterClass { get; set; }

    /// <summary>
    /// Gets or sets the faction reference of the character.
    /// </summary>
    [JsonPropertyName("faction")]
    public BattleNetWowNameRef? Faction { get; set; }

    /// <summary>
    /// Gets or sets the average item level across all equipment slots.
    /// </summary>
    [JsonPropertyName("average_item_level")]
    public double AverageItemLevel { get; set; }

    /// <summary>
    /// Gets or sets the equipped item level.
    /// </summary>
    [JsonPropertyName("equipped_item_level")]
    public double EquippedItemLevel { get; set; }

    /// <summary>
    /// Gets or sets the guild reference, or null when the character is guildless.
    /// </summary>
    [JsonPropertyName("guild")]
    public BattleNetWowNameRef? Guild { get; set; }

    /// <summary>
    /// Gets or sets the unix timestamp (milliseconds) of the last login.
    /// </summary>
    [JsonPropertyName("last_login_timestamp")]
    public long LastLoginTimestamp { get; set; }
}

/// <summary>
/// A named and identified reference object (class, faction, guild).
/// </summary>
internal class BattleNetWowNameRef
{
    /// <summary>
    /// Gets or sets the localized display name.
    /// </summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }
}

/// <summary>
/// A realm reference including its URL slug.
/// </summary>
internal class BattleNetWowRealmRef : BattleNetWowNameRef
{
    /// <summary>
    /// Gets or sets the realm slug used in profile URLs.
    /// </summary>
    [JsonPropertyName("slug")]
    public string? Slug { get; set; }
}

/// <summary>
/// Root container model for the WoW character media response.
/// </summary>
internal class BattleNetWowCharacterMediaResponse
{
    /// <summary>
    /// Gets or sets the media assets (avatar, bust, profile) of the character.
    /// </summary>
    [JsonPropertyName("assets")]
    public List<BattleNetWowMediaAsset>? Assets { get; set; }
}

/// <summary>
/// A single media asset entry.
/// </summary>
internal class BattleNetWowMediaAsset
{
    /// <summary>
    /// Gets or sets the asset key (avatar, bust, profile).
    /// </summary>
    [JsonPropertyName("key")]
    public string? Key { get; set; }

    /// <summary>
    /// Gets or sets the absolute image URL.
    /// </summary>
    [JsonPropertyName("value")]
    public string? Value { get; set; }
}

/// <summary>
/// Root container model for the Diablo III community profile response.
/// </summary>
internal class BattleNetDiabloProfileResponse
{
    /// <summary>
    /// Gets or sets the account BattleTag reported by the profile.
    /// </summary>
    [JsonPropertyName("battleTag")]
    public string? BattleTag { get; set; }

    /// <summary>
    /// Gets or sets the heroes of the account.
    /// </summary>
    [JsonPropertyName("heroes")]
    public List<BattleNetDiabloHeroEntry>? Heroes { get; set; }
}

/// <summary>
/// A single Diablo hero as returned by the community profile endpoint.
/// </summary>
internal class BattleNetDiabloHeroEntry
{
    /// <summary>
    /// Gets or sets the hero name.
    /// </summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>
    /// Gets or sets the hero class identifier (e.g. "wizard").
    /// </summary>
    [JsonPropertyName("class")]
    public string? Class { get; set; }

    /// <summary>
    /// Gets or sets the hero level.
    /// </summary>
    [JsonPropertyName("level")]
    public int? Level { get; set; }

    /// <summary>
    /// Gets or sets the paragon level.
    /// </summary>
    [JsonPropertyName("paragonLevel")]
    public int? ParagonLevel { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the hero is seasonal.
    /// </summary>
    [JsonPropertyName("seasonal")]
    public bool Seasonal { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the hero is hardcore.
    /// </summary>
    [JsonPropertyName("hardcore")]
    public bool Hardcore { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the hero died permanently.
    /// </summary>
    [JsonPropertyName("dead")]
    public bool Dead { get; set; }
}

#endregion
