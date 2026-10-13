namespace WK7Bot.Core.Entities;

/// <summary>
/// Records when a deal alert was last posted for a specific game to enforce the cooldown period.
/// </summary>
public class WishlistAlertHistory
{
    /// <summary>
    /// Gets or sets the unique database identifier.
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Gets or sets the Steam application identifier of the alerted game.
    /// </summary>
    public uint AppId { get; set; }

    /// <summary>
    /// Gets or sets the UTC timestamp when the alert was posted.
    /// </summary>
    public DateTimeOffset AlertedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Gets or sets whether the alert was for a historical low price.
    /// </summary>
    public bool WasHistoricalLow { get; set; }
}
