namespace WK7Bot.Models;

using System;

/// <summary>
/// Represents a single World of Warcraft character of a linked Battle.net account.
/// </summary>
public class BattleNetWowCharacter
{
    /// <summary>
    /// Gets or sets the character's name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the localized realm (server) name.
    /// </summary>
    public string Realm { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the realm slug used in armory URLs.
    /// </summary>
    public string RealmSlug { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the localized character class name (e.g. "Magier").
    /// </summary>
    public string CharacterClass { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the localized faction name (e.g. "Allianz").
    /// </summary>
    public string Faction { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the character level.
    /// </summary>
    public int Level { get; set; }

    /// <summary>
    /// Gets or sets the average item level across all equipment slots.
    /// </summary>
    public double AverageItemLevel { get; set; }

    /// <summary>
    /// Gets or sets the equipped item level.
    /// </summary>
    public double EquippedItemLevel { get; set; }

    /// <summary>
    /// Gets or sets the guild name, or null when the character is guildless.
    /// </summary>
    public string? GuildName { get; set; }

    /// <summary>
    /// Gets or sets the UTC timestamp of the character's last login, or null when unknown.
    /// </summary>
    public DateTimeOffset? LastLoginTimestamp { get; set; }

    /// <summary>
    /// Gets or sets the absolute URL of the character's avatar image, or null when unavailable.
    /// </summary>
    public string? AvatarUrl { get; set; }
}
