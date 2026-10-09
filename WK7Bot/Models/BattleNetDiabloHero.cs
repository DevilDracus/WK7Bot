namespace WK7Bot.Models;

/// <summary>
/// Represents a single Diablo hero of a linked Battle.net account as reported by the
/// Diablo III community profile endpoint.
/// </summary>
public class BattleNetDiabloHero
{
    /// <summary>
    /// Gets or sets the hero's name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the hero class identifier as published by Blizzard (e.g. "wizard").
    /// </summary>
    public string HeroClass { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the hero level (70 at the level cap).
    /// </summary>
    public int Level { get; set; }

    /// <summary>
    /// Gets or sets the hero's paragon level beyond the level cap (0 when none).
    /// </summary>
    public int ParagonLevel { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the hero belongs to the current season.
    /// </summary>
    public bool Seasonal { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the hero is a hardcore hero.
    /// </summary>
    public bool Hardcore { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the hero has died permanently (hardcore only).
    /// </summary>
    public bool Dead { get; set; }
}
