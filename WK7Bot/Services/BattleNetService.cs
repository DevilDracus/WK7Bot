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
using WK7Bot.Models.BattleNet;
using WK7Bot.Options;
using WK7Bot.Services.Interfaces;

namespace WK7Bot.Services;

/// <summary>
/// Communicates with the Battle.net OAuth 2.0 endpoints and Blizzard game APIs to fetch account
/// identity, World of Warcraft characters, and Diablo heroes for linked accounts.
/// </summary>
public partial class BattleNetService : BaseGameApiClient, IBattleNetService
{
    private const int MaxAttemptsValue = 3;
    private const int MaxWowCharacters = 5;

    private readonly HttpClient _httpClient;
    private readonly IMemoryCache _cache;
    private readonly Wk7BotOptions _options;
    private readonly ILogger<BattleNetService> _logger;
    private readonly SemaphoreSlim _apiThrottle = new(2, 2);

    /// <inheritdoc/>
    protected override int MaxAttempts => MaxAttemptsValue;

    /// <summary>
    /// Serializes OAuth token exchanges across all service instances, so concurrent callers share one
    /// refresh grant instead of racing the token endpoint (a second refresh grant could invalidate the
    /// first one). Static because the access-token cache it protects is shared application-wide.
    /// </summary>
    private static readonly SemaphoreSlim TokenRefreshGate = new(1, 1);

    /// <summary>
    /// Gets the base delay applied between retry attempts for transient Battle.net API failures. Virtual for testability.
    /// </summary>
    protected override TimeSpan RetryDelay => TimeSpan.FromMilliseconds(500);

    /// <inheritdoc/>
    protected override SemaphoreSlim ApiThrottle => _apiThrottle;

    /// <inheritdoc/>
    protected override ILogger Logger => _logger;

    /// <inheritdoc/>
    protected override string ServiceName => "Battle.net";

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
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            // External cancellation after the configured timeout. Keep this at warning level and avoid an
            // error-notification storm for a slow external Battle.net endpoint.
            _logger.LogWarning(ex, "Battle.net data retrieval timed out after the configured HTTP timeout.");
            return null;
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
