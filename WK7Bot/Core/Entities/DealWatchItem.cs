namespace WK7Bot.Core.Entities;

/// <summary>
/// Represents a game a Discord user explicitly watches for price drops, independent of their Steam wishlist.
/// </summary>
public class DealWatchItem
{
    /// <summary>Gets or sets the unique database identifier.</summary>
    public int Id { get; set; }

    /// <summary>Gets or sets the Discord user identifier that owns this watch.</summary>
    public ulong DiscordUserId { get; set; }

    /// <summary>Gets or sets the game name as entered by the user.</summary>
    public string GameName { get; set; } = string.Empty;

    /// <summary>Gets or sets the resolved IsThereAnyDeal identifier, when available.</summary>
    public string? ItadId { get; set; }

    /// <summary>Gets or sets the minimum discount percentage that triggers an alert.</summary>
    public int MinDiscountPercent { get; set; } = 40;

    /// <summary>Gets or sets the optional absolute target price.</summary>
    public decimal? TargetPrice { get; set; }

    /// <summary>Gets or sets the last observed price.</summary>
    public decimal? LastKnownPrice { get; set; }

    /// <summary>Gets or sets the last observed discount percentage.</summary>
    public int LastKnownDiscount { get; set; }

    /// <summary>Gets or sets the UTC timestamp when the watch was created.</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Gets or sets the UTC timestamp of the last alert sent for this watch.</summary>
    public DateTime? LastAlertedAt { get; set; }
}
