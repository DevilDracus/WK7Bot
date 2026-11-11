namespace WK7Bot.Core.Entities;

/// <summary>
/// Represents a Steam AppID a Discord user explicitly added to watch for deals, bypassing the public wishlist requirement.
/// </summary>
public class ManualWishlistItem
{
    /// <summary>Gets or sets the unique database identifier.</summary>
    public int Id { get; set; }

    /// <summary>Gets or sets the Discord user identifier that owns this entry.</summary>
    public ulong DiscordUserId { get; set; }

    /// <summary>Gets or sets the Steam application identifier.</summary>
    public uint AppId { get; set; }

    /// <summary>Gets or sets the game name, if known.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the UTC timestamp when the entry was created.</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
