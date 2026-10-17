namespace WK7Bot.Services;

using Microsoft.Extensions.Options;
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
        var merged = new Dictionary<ulong, string>();
        foreach (var mapping in _options.Value.DiscordSteamMappings)
        {
            if (ulong.TryParse(mapping.DiscordUserId, out var id) && !string.IsNullOrWhiteSpace(mapping.SteamId))
            {
                merged[id] = mapping.SteamId;
            }
        }

        foreach (var link in await _repository.GetAllAsync(cancellationToken).ConfigureAwait(false))
        {
            if (link.Provider == "steam" && !string.IsNullOrWhiteSpace(link.ExternalId))
            {
                merged[link.DiscordUserId] = link.ExternalId;
            }
        }

        return merged.Select(kv => new SteamAccountLink(kv.Key, kv.Value)).ToList();
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
            if (link.Provider == "battlenet" && !string.IsNullOrWhiteSpace(link.RefreshToken))
            {
                merged[link.DiscordUserId] = new BattleNetAccountLink(link.DiscordUserId, link.RefreshToken!, link.Region, link.ExternalId);
            }
        }

        return merged.Values.ToList();
    }

    /// <inheritdoc />
    public async Task<string?> GetSteamIdAsync(ulong discordUserId, CancellationToken cancellationToken = default)
    {
        var link = await _repository.GetAsync(discordUserId, "steam", cancellationToken).ConfigureAwait(false);
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
        var link = await _repository.GetAsync(discordUserId, "battlenet", cancellationToken).ConfigureAwait(false);
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
