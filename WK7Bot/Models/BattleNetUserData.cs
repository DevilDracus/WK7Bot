namespace WK7Bot.Models;

using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Aggregates Battle.net account identity plus the per-game profile data the API exposes for it
/// (World of Warcraft characters and Diablo heroes), mirroring <see cref="SteamUserData"/>.
/// </summary>
public class BattleNetUserData
{
    /// <summary>
    /// Gets or sets the numeric Battle.net account identifier returned by the OAuth userinfo endpoint.
    /// </summary>
    public string BattleNetId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the account's BattleTag (e.g. "Arthas#21234").
    /// </summary>
    public string BattleTag { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the API region used for this account (us, eu, kr, tw).
    /// </summary>
    public string Region { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the avatar image URL of the main World of Warcraft character, or null when unavailable.
    /// </summary>
    public string? AvatarUrl { get; set; }

    /// <summary>
    /// Gets or sets the World of Warcraft characters of the account, main character first.
    /// </summary>
    public List<BattleNetWowCharacter> WowCharacters { get; set; } = new();

    /// <summary>
    /// Gets or sets the Diablo heroes of the account. Empty when Blizzard reports no heroes or the
    /// Diablo III community profile endpoint is unavailable.
    /// </summary>
    public List<BattleNetDiabloHero> DiabloHeroes { get; set; } = new();

    /// <summary>
    /// Gets the short human-readable label of the main World of Warcraft character
    /// (e.g. "Lyria · Stufe 80"), or null when the account has no characters.
    /// </summary>
    public string? MainCharacterDisplay
    {
        get
        {
            var main = WowCharacters.FirstOrDefault();
            return main is null ? null : $"{main.Name} · Stufe {main.Level}";
        }
    }
}
