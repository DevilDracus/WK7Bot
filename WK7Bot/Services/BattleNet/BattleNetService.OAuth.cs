using System.Net.Http.Headers;
using System.Text;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using WK7Bot.Models;
using WK7Bot.Models.BattleNet;
using WK7Bot.Options;
namespace WK7Bot.Services;

public partial class BattleNetService
{
    private const int DefaultTokenExpirySeconds = 3600;
    private const int MinimumCacheTtlSeconds = 60;
    private const int TokenExpirySafetyMarginSeconds = 300;

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

            int expiresIn = payload.ExpiresIn > 0 ? payload.ExpiresIn : DefaultTokenExpirySeconds;
            var cacheTtl = TimeSpan.FromSeconds(Math.Max(MinimumCacheTtlSeconds, expiresIn - TokenExpirySafetyMarginSeconds));
            _cache.Set(cacheKey, payload.AccessToken!, new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = cacheTtl, Size = 1 });

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
}
