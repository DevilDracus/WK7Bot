namespace WK7Bot.Services;

using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WK7Bot.Core.Constants;
using WK7Bot.Core.Entities;
using WK7Bot.Core.Interfaces;
using WK7Bot.Core.Utilities;
using WK7Bot.Options;
using WK7Bot.Services.Interfaces;

/// <summary>
/// Stores Discord user to Steam/Battle.net links and mirrors them into the add-on options so the
/// mapping is visible in the Home Assistant add-on configuration.
/// </summary>
public class AccountLinkService : IAccountLinkService
{
    private readonly IUserAccountLinkRepository _repository;
    private readonly AddOnOptionsWriter _optionsWriter;
    private readonly IOptions<Wk7BotOptions> _options;
    private readonly HttpClient _httpClient;
    private readonly ILogger<AccountLinkService> _logger;

    /// <summary>Initializes a new instance of the <see cref="AccountLinkService"/> class.</summary>
    public AccountLinkService(
        IUserAccountLinkRepository repository,
        AddOnOptionsWriter optionsWriter,
        IOptions<Wk7BotOptions> options,
        HttpClient httpClient,
        ILogger<AccountLinkService> logger)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _optionsWriter = optionsWriter ?? throw new ArgumentNullException(nameof(optionsWriter));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<AccountLinkResult> LinkSteamAsync(ulong discordUserId, string input, CancellationToken cancellationToken = default)
    {
        var steamId = await ResolveSteamIdAsync(input, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(steamId))
        {
            return new AccountLinkResult(false, "Kein gültiger Steam-Account gefunden. Nutze eine SteamID64, eine Profil-URL oder eine Wishlist-URL.");
        }

        await _repository.UpsertAsync(new UserAccountLink
        {
            DiscordUserId = discordUserId,
            Provider = AccountProviders.Steam,
            ExternalId = steamId,
            DisplayName = null,
            WishlistUrl = input.Contains("wishlist", StringComparison.OrdinalIgnoreCase) ? input : null,
            LinkedAt = DateTime.UtcNow
        }, cancellationToken).ConfigureAwait(false);

        await MirrorSteamMappingAsync(discordUserId, steamId, cancellationToken).ConfigureAwait(false);
        return new AccountLinkResult(true, $"Steam-Account verknüpft: `{steamId}`.", steamId);
    }

    /// <inheritdoc />
    public async Task<AccountLinkResult> LinkBattleNetAsync(ulong discordUserId, string refreshToken, string? region, string? battleTag, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            return new AccountLinkResult(false, "Ein Battle.net Refresh-Token ist erforderlich.");
        }

        var effectiveRegion = string.IsNullOrWhiteSpace(region) ? _options.Value.BattleNetRegion : region!;
        await _repository.UpsertAsync(new UserAccountLink
        {
            DiscordUserId = discordUserId,
            Provider = AccountProviders.BattleNet,
            ExternalId = battleTag ?? "Battle.net",
            RefreshToken = refreshToken,
            Region = effectiveRegion,
            LinkedAt = DateTime.UtcNow
        }, cancellationToken).ConfigureAwait(false);

        await MirrorBattleNetMappingAsync(discordUserId, refreshToken, effectiveRegion, battleTag, cancellationToken).ConfigureAwait(false);
        return new AccountLinkResult(true, "Battle.net-Account verknüpft.", battleTag);
    }

    /// <inheritdoc />
    public async Task<AccountLinkResult> LinkWishlistAsync(ulong discordUserId, string wishlistUrl, CancellationToken cancellationToken = default)
    {
        var steamId = await ResolveSteamIdAsync(wishlistUrl, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(steamId))
        {
            return new AccountLinkResult(false, "Die Wishlist-URL konnte keinem Steam-Account zugeordnet werden.");
        }

        await _repository.UpsertAsync(new UserAccountLink
        {
            DiscordUserId = discordUserId,
            Provider = AccountProviders.Steam,
            ExternalId = steamId,
            WishlistUrl = wishlistUrl,
            LinkedAt = DateTime.UtcNow
        }, cancellationToken).ConfigureAwait(false);

        await MirrorSteamMappingAsync(discordUserId, steamId, cancellationToken).ConfigureAwait(false);
        return new AccountLinkResult(true, $"Wishlist verknüpft: `{steamId}`.", steamId);
    }

    /// <inheritdoc />
    public async Task<AccountLinkResult> UnlinkAsync(ulong discordUserId, string provider, CancellationToken cancellationToken = default)
    {
        var removed = await _repository.RemoveAsync(discordUserId, provider, cancellationToken).ConfigureAwait(false);
        return removed
            ? new AccountLinkResult(true, $"Verknüpfung für `{provider}` entfernt.")
            : new AccountLinkResult(false, $"Keine Verknüpfung für `{provider}` gefunden.");
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<UserAccountLink>> GetLinksAsync(ulong discordUserId, CancellationToken cancellationToken = default)
        => _repository.GetForUserAsync(discordUserId, cancellationToken);

    private async Task<string?> ResolveSteamIdAsync(string input, CancellationToken cancellationToken)
    {
        if (SteamUrlParser.TryGetSteamId(input, out var steamId))
        {
            return steamId;
        }

        if (!SteamUrlParser.TryGetVanity(input, out var vanity) || string.IsNullOrWhiteSpace(vanity))
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(_options.Value.SteamApiKey))
        {
            return null;
        }

        var url = $"https://api.steampowered.com/ISteamUser/ResolveVanityURL/v0001/?key={_options.Value.SteamApiKey}&vanityurl={Uri.EscapeDataString(vanity)}";
        try
        {
            using var response = await HttpRetry.ExecuteAsync(
                () => _httpClient.GetAsync(url, cancellationToken),
                3,
                r => (int)r.StatusCode >= 500 || r.StatusCode == System.Net.HttpStatusCode.TooManyRequests,
                attempt => TimeSpan.FromMilliseconds(500 * (1 << (attempt - 1))),
                null,
                null,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
            var root = document.RootElement;
            if (root.TryGetProperty("response", out var resolved) &&
                resolved.TryGetProperty("success", out var success) &&
                success.GetInt32() == 1 &&
                resolved.TryGetProperty("steamid", out var id))
            {
                return id.GetString();
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Steam vanity resolution failed for {Vanity}.", vanity);
        }

        return null;
    }

    private async Task MirrorSteamMappingAsync(ulong discordUserId, string steamId, CancellationToken cancellationToken)
    {
        if (!_options.Value.LinkAutoWriteOptions)
        {
            return;
        }

        var current = await _optionsWriter.ReadCurrentOptionsAsync().ConfigureAwait(false);
        var mappings = current["discord_steam_mappings"]?.AsArray() ?? new System.Text.Json.Nodes.JsonArray();
        var already = mappings.Any(node =>
            node is System.Text.Json.Nodes.JsonObject obj &&
            obj["discord_user_id"]?.GetValue<string>() == discordUserId.ToString());
        if (!already)
        {
            mappings.Add(new System.Text.Json.Nodes.JsonObject
            {
                ["discord_user_id"] = discordUserId.ToString(),
                ["steam_id"] = steamId
            });
        }

        await _optionsWriter.MergeAsync(new Dictionary<string, object?>
        {
            ["discord_steam_mappings"] = JsonSerializer.Deserialize<object>(mappings.ToJsonString())
        }, cancellationToken).ConfigureAwait(false);
    }

    private async Task MirrorBattleNetMappingAsync(ulong discordUserId, string refreshToken, string region, string? battleTag, CancellationToken cancellationToken)
    {
        if (!_options.Value.LinkAutoWriteOptions)
        {
            return;
        }

        var current = await _optionsWriter.ReadCurrentOptionsAsync().ConfigureAwait(false);
        var mappings = current["discord_battlenet_mappings"]?.AsArray() ?? new System.Text.Json.Nodes.JsonArray();
        var already = mappings.Any(node =>
            node is System.Text.Json.Nodes.JsonObject obj &&
            obj["discord_user_id"]?.GetValue<string>() == discordUserId.ToString());
        if (!already)
        {
            mappings.Add(new System.Text.Json.Nodes.JsonObject
            {
                ["discord_user_id"] = discordUserId.ToString(),
                ["refresh_token"] = refreshToken,
                ["region"] = region,
                ["battle_tag"] = battleTag
            });
        }

        await _optionsWriter.MergeAsync(new Dictionary<string, object?>
        {
            ["discord_battlenet_mappings"] = JsonSerializer.Deserialize<object>(mappings.ToJsonString())
        }, cancellationToken).ConfigureAwait(false);
    }
}
