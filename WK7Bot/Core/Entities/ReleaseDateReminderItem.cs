namespace WK7Bot.Core.Entities;

/// <summary>
/// Represents a Steam game a Discord user wants to be reminded about when its release date approaches.
/// </summary>
public class ReleaseDateReminderItem
{
    /// <summary>Gets or sets the unique database identifier.</summary>
    public int Id { get; set; }

    /// <summary>Gets or sets the Discord user identifier that owns this reminder.</summary>
    public ulong DiscordUserId { get; set; }

    /// <summary>Gets or sets the Steam application identifier.</summary>
    public uint AppId { get; set; }

    /// <summary>Gets or sets the game name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the known release date. May be null if the date is not yet announced.</summary>
    public DateTime? ReleaseDate { get; set; }

    /// <summary>Gets or sets whether the release date is currently unknown or "TBA".</summary>
    public bool IsTba { get; set; }

    /// <summary>Gets or sets the UTC timestamp when the reminder was created.</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Gets or sets the UTC timestamp of the last notification sent for this reminder.</summary>
    public DateTime? LastNotifiedAt { get; set; }
}
