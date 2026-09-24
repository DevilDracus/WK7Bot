namespace WK7Bot.Models;

using System;

/// <summary>
/// Represents the detailed presence state, activity details, and avatar assets of a Discord user for MQTT integration.
/// </summary>
public class UserPresenceEntity
{
    /// <summary>
    /// Gets or sets the Discord username.
    /// </summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the unique Discord user identifier snowflake.
    /// </summary>
    public ulong DiscordUserId { get; set; }

    /// <summary>
    /// Gets or sets the current online status indicator such as Online, Idle, DoNotDisturb, or Offline.
    /// </summary>
    public string Status { get; set; } = "Offline";

    /// <summary>
    /// Gets or sets the direct CDN URL pointing to the user's active avatar image.
    /// </summary>
    public string AvatarUrl { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the display name of the game or application currently being played.
    /// </summary>
    public string? GameName { get; set; }

    /// <summary>
    /// Gets or sets extended rich presence status or activity details provided by the active application.
    /// </summary>
    public string? GameDetails { get; set; }

    /// <summary>
    /// Gets or sets the image asset URL for the active game thumbnail or rich presence artwork.
    /// </summary>
    public string? GameThumbnailUrl { get; set; }

    /// <summary>
    /// Gets or sets the UTC timestamp when the presence snapshot was captured and published.
    /// </summary>
    public DateTimeOffset LastUpdated { get; set; } = DateTimeOffset.UtcNow;
}