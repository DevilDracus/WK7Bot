namespace WK7Bot.Core.Entities;

/// <summary>
/// Represents a game that appears on at least one mapped user's Steam wishlist.
/// Used to maintain the deduplicated set of tracked games for price monitoring.
/// </summary>
public class WishlistGame
{
    /// <summary>
    /// Gets or sets the unique database identifier.
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Gets or sets the Steam application identifier.
    /// </summary>
    public uint AppId { get; set; }

    /// <summary>
    /// Gets or sets the display name of the game.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the UTC timestamp when this game was last discovered on a wishlist.
    /// </summary>
    public DateTimeOffset LastUpdated { get; set; } = DateTimeOffset.UtcNow;
}
