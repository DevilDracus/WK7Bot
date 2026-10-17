namespace WK7Bot.Services.Interfaces;

/// <summary>
/// Builds a rich gaming profile card for one Discord member by combining the Steam and Battle.net
/// data of the accounts linked to them.
/// </summary>
public interface IProfileService
{
    /// <summary>
    /// Resolves the linked accounts of a member and fetches their profile data.
    /// </summary>
    /// <param name="discordUserId">The Discord user whose profile is requested.</param>
    /// <param name="guildId">The guild used to resolve the member display name.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The profile; both sections are <see langword="null"/> when nothing is linked.</returns>
    Task<ProfileResult> GetAsync(ulong discordUserId, ulong guildId, CancellationToken cancellationToken = default);
}

/// <summary>A rendered member profile.</summary>
/// <param name="DisplayName">The Discord display name (or the user ID as fallback).</param>
/// <param name="Steam">The Steam section, or <see langword="null"/> when no Steam account is linked.</param>
/// <param name="BattleNet">The Battle.net section, or <see langword="null"/> when no Battle.net account is linked.</param>
public sealed record ProfileResult(string DisplayName, SteamProfileSection? Steam, BattleNetProfileSection? BattleNet);

/// <summary>Steam profile data of a linked member.</summary>
/// <param name="PersonaName">The Steam persona name.</param>
/// <param name="AvatarUrl">The full-size avatar URL, or <see langword="null"/>.</param>
/// <param name="PersonaState">The current persona state.</param>
/// <param name="CurrentGameTitle">The game currently being played, or <see langword="null"/>.</param>
/// <param name="PlaytimeLastTwoWeeksMinutes">Playtime across all games in the last 14 days.</param>
/// <param name="PlaytimeDisplay">The formatted playtime.</param>
/// <param name="AchievementsUnlocked">Achievements unlocked across the recent games.</param>
/// <param name="AchievementsTotal">Achievements defined across the recent games.</param>
/// <param name="TopRecentGames">The most-played recent games.</param>
public sealed record SteamProfileSection(
    string PersonaName,
    string? AvatarUrl,
    string PersonaState,
    string? CurrentGameTitle,
    int PlaytimeLastTwoWeeksMinutes,
    string PlaytimeDisplay,
    int AchievementsUnlocked,
    int AchievementsTotal,
    IReadOnlyList<SteamProfileGame> TopRecentGames);

/// <summary>One recent Steam game row.</summary>
/// <param name="Name">The game title.</param>
/// <param name="PlaytimeTwoWeeksMinutes">Playtime in the last 14 days.</param>
/// <param name="PlaytimeForeverMinutes">Lifetime playtime.</param>
/// <param name="AchievementsUnlocked">Unlocked achievements.</param>
/// <param name="AchievementsTotal">Defined achievements.</param>
public sealed record SteamProfileGame(
    string Name,
    int PlaytimeTwoWeeksMinutes,
    int PlaytimeForeverMinutes,
    int AchievementsUnlocked,
    int AchievementsTotal);

/// <summary>Battle.net profile data of a linked member.</summary>
/// <param name="BattleTag">The account's BattleTag.</param>
/// <param name="Region">The API region.</param>
/// <param name="WowCharacters">The World of Warcraft characters, main first.</param>
/// <param name="DiabloHeroes">The Diablo heroes.</param>
public sealed record BattleNetProfileSection(
    string BattleTag,
    string Region,
    IReadOnlyList<WowProfileCharacter> WowCharacters,
    IReadOnlyList<DiabloProfileHero> DiabloHeroes);

/// <summary>One World of Warcraft character row.</summary>
/// <param name="Name">The character name.</param>
/// <param name="Realm">The realm name.</param>
/// <param name="CharacterClass">The localized class name.</param>
/// <param name="Faction">The localized faction name.</param>
/// <param name="Level">The character level.</param>
/// <param name="AverageItemLevel">The average item level.</param>
/// <param name="GuildName">The guild name, or <see langword="null"/>.</param>
/// <param name="IsMain">Whether this is the account's main character.</param>
public sealed record WowProfileCharacter(
    string Name,
    string Realm,
    string CharacterClass,
    string Faction,
    int Level,
    double AverageItemLevel,
    string? GuildName,
    bool IsMain);

/// <summary>One Diablo hero row.</summary>
/// <param name="Name">The hero name.</param>
/// <param name="HeroClass">The hero class.</param>
/// <param name="Level">The hero level.</param>
/// <param name="ParagonLevel">The paragon level.</param>
/// <param name="Seasonal">Whether the hero is seasonal.</param>
/// <param name="Hardcore">Whether the hero is hardcore.</param>
/// <param name="Dead">Whether a hardcore hero has died.</param>
public sealed record DiabloProfileHero(
    string Name,
    string HeroClass,
    int Level,
    int ParagonLevel,
    bool Seasonal,
    bool Hardcore,
    bool Dead);
