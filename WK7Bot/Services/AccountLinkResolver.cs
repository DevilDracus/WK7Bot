namespace WK7Bot.Services;

using Microsoft.Extensions.Options;
using WK7Bot.Core.Constants;
using WK7Bot.Core.Interfaces;
using WK7Bot.Options;
using WK7Bot.Services.Interfaces;

/// <summary>
/// Merges the static option mappings with the runtime account links stored in SQLite. Runtime links
/// win, which is what makes <c>/link</c> and <c>/wishlist-add</c> effective immediately.
/// </summary>
public class AccountLinkResolver : IAccountLinkResolver
{
    private readonly IUserAccountLinkRepository _repository;
    private readonly IOptions<Wk7BotOptions> _options;

    /// <summary>Initializes a new instance of the <see cref="AccountLinkResolver"/> class.</summary>
    /// <param name="repository">The account link repository.</param>
    /// <param name="options">The application options containing the static mappings.</param>
    public AccountLinkResolver(IUserAccountLinkRepository repository, IOptions<Wk7BotOptions> options)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SteamAccountLink>> GetSteamLinksAsync(CancellationToken cancellationToken = default)
    {
        var merged = new Dictionary<ulong, (string SteamId, string? WishlistUrl)>();
        foreach (var mapping in _options.Value.DiscordSteamMappings)
        {
            if (ulong.TryParse(mapping.DiscordUserId, out var id) && !string.IsNullOrWhiteSpace(mapping.SteamId))
            {
                merged[id] = (mapping.SteamId, null);
            }
        }

        foreach (var link in await _repository.GetAllAsync(cancellationToken).ConfigureAwait(false))
        {
            if (link.Provider == AccountProviders.Steam && !string.IsNullOrWhiteSpace(link.ExternalId))
            {
                merged[link.DiscordUserId] = (link.ExternalId, link.WishlistUrl);
            }
        }

        return merged.Select(kv => new SteamAccountLink(kv.Key, kv.Value.SteamId, kv.Value.WishlistUrl)).ToList();
    }

    /// <inheritdoc />
    public async Task<SteamAccountLink?> GetSteamLinkAsync(ulong discordUserId, CancellationToken cancellationToken = default)
    {
        var links = await GetSteamLinksAsync(cancellationToken).ConfigureAwait(false);
        return links.FirstOrDefault(l => l.DiscordUserId == discordUserId);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<BattleNetAccountLink>> GetBattleNetLinksAsync(CancellationToken cancellationToken = default)
    {
        var merged = new Dictionary<ulong, BattleNetAccountLink>();
        foreach (var mapping in _options.Value.DiscordBattleNetMappings)
        {
            if (ulong.TryParse(mapping.DiscordUserId, out var id) && !string.IsNullOrWhiteSpace(mapping.RefreshToken))
            {
                merged[id] = new BattleNetAccountLink(id, mapping.RefreshToken, mapping.Region, mapping.BattleTag);
            }
        }

        foreach (var link in await _repository.GetAllAsync(cancellationToken).ConfigureAwait(false))
        {
            if (link.Provider == AccountProviders.BattleNet && !string.IsNullOrWhiteSpace(link.RefreshToken))
            {
                merged[link.DiscordUserId] = new BattleNetAccountLink(link.DiscordUserId, link.RefreshToken!, link.Region, link.ExternalId);
            }
        }

        return merged.Values.ToList();
    }

    /// <summary>
    /// Returns all account links for all providers in a single query, split by provider.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token to observe.</param>
    /// <returns>A tuple of Steam links and Battle.net links.</returns>
    public async Task<(IReadOnlyList<SteamAccountLink> Steam, IReadOnlyList<BattleNetAccountLink> BattleNet)> GetAllLinksAsync(CancellationToken cancellationToken = default)
    {
        var allLinks = await _repository.GetAllAsync(cancellationToken).ConfigureAwait(false);

        var steamMerged = new Dictionary<ulong, (string SteamId, string? WishlistUrl)>();
        foreach (var mapping in _options.Value.DiscordSteamMappings)
        {
            if (ulong.TryParse(mapping.DiscordUserId, out var id) && !string.IsNullOrWhiteSpace(mapping.SteamId))
            {
                steamMerged[id] = (mapping.SteamId, null);
            }
        }

        var battleNetMerged = new Dictionary<ulong, BattleNetAccountLink>();
        foreach (var mapping in _options.Value.DiscordBattleNetMappings)
        {
            if (ulong.TryParse(mapping.DiscordUserId, out var id) && !string.IsNullOrWhiteSpace(mapping.RefreshToken))
            {
                battleNetMerged[id] = new BattleNetAccountLink(id, mapping.RefreshToken, mapping.Region, mapping.BattleTag);
            }
        }

        foreach (var link in allLinks)
        {
            if (link.Provider == AccountProviders.Steam && !string.IsNullOrWhiteSpace(link.ExternalId))
            {
                steamMerged[link.DiscordUserId] = (link.ExternalId, link.WishlistUrl);
            }
            else if (link.Provider == AccountProviders.BattleNet && !string.IsNullOrWhiteSpace(link.RefreshToken))
            {
                battleNetMerged[link.DiscordUserId] = new BattleNetAccountLink(link.DiscordUserId, link.RefreshToken!, link.Region, link.ExternalId);
            }
        }

        var steamResult = steamMerged.Select(kv => new SteamAccountLink(kv.Key, kv.Value.SteamId, kv.Value.WishlistUrl)).ToList();
        var battleNetResult = battleNetMerged.Values.ToList();

        return (steamResult, battleNetResult);
    }

    /// <inheritdoc />
    public async Task<string?> GetSteamIdAsync(ulong discordUserId, CancellationToken cancellationToken = default)
    {
        var link = await _repository.GetAsync(discordUserId, AccountProviders.Steam, cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(link?.ExternalId))
        {
            return link!.ExternalId;
        }

        var mapping = _options.Value.DiscordSteamMappings
            .FirstOrDefault(m => ulong.TryParse(m.DiscordUserId, out var id) && id == discordUserId);
        return string.IsNullOrWhiteSpace(mapping?.SteamId) ? null : mapping!.SteamId;
    }

    /// <inheritdoc />
    public async Task<BattleNetAccountLink?> GetBattleNetLinkAsync(ulong discordUserId, CancellationToken cancellationToken = default)
    {
        var link = await _repository.GetAsync(discordUserId, AccountProviders.BattleNet, cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(link?.RefreshToken))
        {
            return new BattleNetAccountLink(link!.DiscordUserId, link.RefreshToken!, link.Region, link.ExternalId);
        }

        var mapping = _options.Value.DiscordBattleNetMappings
            .FirstOrDefault(m => ulong.TryParse(m.DiscordUserId, out var id) && id == discordUserId);
        return mapping is null || string.IsNullOrWhiteSpace(mapping.RefreshToken)
            ? null
            : new BattleNetAccountLink(discordUserId, mapping.RefreshToken, mapping.Region, mapping.BattleTag);
    }
}
