namespace WK7Bot.Core.Entities;

/// <summary>
/// A single recorded price observation for a game at a specific store.
/// </summary>
public class PriceHistorySnapshot
{
    /// <summary>
    /// Gets or sets the database identifier.
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
    /// Gets or sets the store name (e.g. Steam, GOG, Epic).
    /// </summary>
    public string Store { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the observed price in the configured currency.
    /// </summary>
    public decimal Price { get; set; }

    /// <summary>
    /// Gets or sets the regular non-discounted price, used to calculate the discount percentage.
    /// </summary>
    public decimal? RegularPrice { get; set; }

    /// <summary>
    /// Gets or sets the discount percentage when the snapshot was taken.
    /// </summary>
    public int DiscountPercent { get; set; }

    /// <summary>
    /// Gets or sets the ISO 4217 currency code (e.g. EUR).
    /// </summary>
    public string Currency { get; set; } = "EUR";

    /// <summary>
    /// Gets or sets the UTC timestamp when the snapshot was recorded.
    /// </summary>
    public DateTime RecordedAt { get; set; }
}
