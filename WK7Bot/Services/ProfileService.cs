namespace WK7Bot.Services;

using Discord.WebSocket;
using WK7Bot.Models;
using WK7Bot.Services.Interfaces;

/// <summary>
/// Builds a member profile card from the Steam and Battle.net accounts linked to them. Links are
/// resolved through <see cref="IAccountLinkResolver"/>, so both <c>/link</c> mappings and the
/// static add-on options are honoured.
/// </summary>
public class ProfileService : IProfileService
{
    private const int MaxRecentGames = 5;
    private const int MaxWowCharacters = 5;
    private const int MaxDiabloHeroes = 5;

    private readonly IAccountLinkResolver _accountLinkResolver;
    private readonly ISteamService _steamService;
    private readonly IBattleNetService _battleNetService;
    private readonly DiscordSocketClient _discordClient;
    private readonly ILogger<ProfileService> _logger;

    /// <summary>Initializes a new instance of the <see cref="ProfileService"/> class.</summary>
    /// <param name="accountLinkResolver">The merged account-link resolver.</param>
    /// <param name="steamService">The Steam service.</param>
    /// <param name="battleNetService">The Battle.net service.</param>
    /// <param name="discordClient">The Discord client used to resolve display names.</param>
    /// <param name="logger">The logger instance.</param>
    public ProfileService(
        IAccountLinkResolver accountLinkResolver,
        ISteamService steamService,
        IBattleNetService battleNetService,
        DiscordSocketClient discordClient,
        ILogger<ProfileService> logger)
    {
        _accountLinkResolver = accountLinkResolver ?? throw new ArgumentNullException(nameof(accountLinkResolver));
        _steamService = steamService ?? throw new ArgumentNullException(nameof(steamService));
        _battleNetService = battleNetService ?? throw new ArgumentNullException(nameof(battleNetService));
        _discordClient = discordClient ?? throw new ArgumentNullException(nameof(discordClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<ProfileResult> GetAsync(ulong discordUserId, ulong guildId, CancellationToken cancellationToken = default)
    {
        var displayName = _discordClient.GetGuild(guildId)?.GetUser(discordUserId)?.DisplayName
                          ?? discordUserId.ToString();

        var steam = await BuildSteamSectionAsync(discordUserId, cancellationToken).ConfigureAwait(false);
        var battleNet = await BuildBattleNetSectionAsync(discordUserId, cancellationToken).ConfigureAwait(false);

        return new ProfileResult(displayName, steam, battleNet);
    }

    private async Task<SteamProfileSection?> BuildSteamSectionAsync(ulong discordUserId, CancellationToken cancellationToken)
    {
        var steamId = await _accountLinkResolver.GetSteamIdAsync(discordUserId, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(steamId))
        {
            return null;
        }

        SteamUserData? data;
        try
        {
            data = await _steamService.GetSteamUserDataAsync(steamId, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Could not fetch Steam profile data for Discord user {DiscordUserId}.", discordUserId);
            return null;
        }

        if (data is null)
        {
            return null;
        }

        var games = data.RecentGames
            .OrderByDescending(game => game.PlaytimeTwoWeeksMinutes)
            .ThenByDescending(game => game.PlaytimeForeverMinutes)
            .Take(MaxRecentGames)
            .Select(game => new SteamProfileGame(
                game.Name,
                game.PlaytimeTwoWeeksMinutes,
                game.PlaytimeForeverMinutes,
                game.AchievementsUnlocked,
                game.AchievementsTotal))
            .ToList();

        return new SteamProfileSection(
            data.PersonaName,
            data.SteamAvatarUrl,
            data.PersonaState,
            data.CurrentGameTitle,
            data.PlaytimeLastTwoWeeksMinutes,
            data.PlaytimeDisplay,
            data.RecentGames.Sum(game => game.AchievementsUnlocked),
            data.RecentGames.Sum(game => game.AchievementsTotal),
            games);
    }

    private async Task<BattleNetProfileSection?> BuildBattleNetSectionAsync(ulong discordUserId, CancellationToken cancellationToken)
    {
        var link = await _accountLinkResolver.GetBattleNetLinkAsync(discordUserId, cancellationToken).ConfigureAwait(false);
        if (link is null)
        {
            return null;
        }

        BattleNetUserData? data;
        try
        {
            data = await _battleNetService.GetBattleNetUserDataAsync(link.RefreshToken, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Could not fetch Battle.net profile data for Discord user {DiscordUserId}.", discordUserId);
            return null;
        }

        if (data is null)
        {
            return null;
        }

        var characters = data.WowCharacters
            .OrderByDescending(character => character.Level)
            .ThenByDescending(character => character.AverageItemLevel)
            .Take(MaxWowCharacters)
            .Select((character, index) => new WowProfileCharacter(
                character.Name,
                character.Realm,
                character.CharacterClass,
                character.Faction,
                character.Level,
                character.AverageItemLevel,
                character.GuildName,
                index == 0))
            .ToList();

        var heroes = data.DiabloHeroes
            .OrderByDescending(hero => hero.ParagonLevel)
            .ThenByDescending(hero => hero.Level)
            .Take(MaxDiabloHeroes)
            .Select(hero => new DiabloProfileHero(
                hero.Name,
                hero.HeroClass,
                hero.Level,
                hero.ParagonLevel,
                hero.Seasonal,
                hero.Hardcore,
                hero.Dead))
            .ToList();

        return new BattleNetProfileSection(
            string.IsNullOrWhiteSpace(data.BattleTag) ? link.BattleTag ?? "Battle.net" : data.BattleTag,
            string.IsNullOrWhiteSpace(data.Region) ? link.Region ?? string.Empty : data.Region,
            characters,
            heroes);
    }
}
