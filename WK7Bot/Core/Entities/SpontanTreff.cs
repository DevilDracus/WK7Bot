namespace WK7Bot.Core.Entities;

/// <summary>
/// A short-lived spontaneous meetup ("Spontan-Treff") that is announced with on-my-way and pass buttons.
/// </summary>
public class SpontanTreff
{
    /// <summary>
    /// Gets or sets the database identifier used inside the button custom IDs.
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Gets or sets the Discord guild the meetup was created in.
    /// </summary>
    public ulong GuildId { get; set; }

    /// <summary>
    /// Gets or sets the Discord channel that contains the meetup message.
    /// </summary>
    public ulong ChannelId { get; set; }

    /// <summary>
    /// Gets or sets the Discord message ID of the interactive meetup message; zero until it has been posted.
    /// </summary>
    public ulong MessageId { get; set; }

    /// <summary>
    /// Gets or sets the Discord user ID of the organizer who created the meetup.
    /// </summary>
    public ulong OrganizerId { get; set; }

    /// <summary>
    /// Gets or sets the display name of the organizer at creation time.
    /// </summary>
    public string OrganizerName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the free-text plan, e.g. "Kurzer Abendspaziergang".
    /// </summary>
    public string Plan { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the optional meeting point, e.g. "Innenhof".
    /// </summary>
    public string? Location { get; set; }

    /// <summary>
    /// Gets or sets the local time the meetup was created.
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// Gets or sets the local time after which the meetup is closed and the buttons stop working.
    /// </summary>
    public DateTime ExpiresAt { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the meetup was closed before its expiry (organizer or background service).
    /// </summary>
    public bool Closed { get; set; }
}
