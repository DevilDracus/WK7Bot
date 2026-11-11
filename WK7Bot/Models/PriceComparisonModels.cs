namespace WK7Bot.Models;

/// <summary>
/// Represents a single store offer for a game.
/// </summary>
public sealed class StorePriceOffer
{
    /// <summary>The store name.</summary>
    public string StoreName { get; set; } = string.Empty;

    /// <summary>The current price.</summary>
    public decimal Price { get; set; }

    /// <summary>The regular price.</summary>
    public decimal RegularPrice { get; set; }

    /// <summary>The discount percentage.</summary>
    public int DiscountPercent { get; set; }

    /// <summary>The deal URL.</summary>
    public string DealUrl { get; set; } = string.Empty;
}

/// <summary>
/// Price comparison result for a single game across multiple stores.
/// </summary>
public sealed class PriceComparisonResult
{
    /// <summary>The game title.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>The Steam application identifier.</summary>
    public uint AppId { get; set; }

    /// <summary>The offers ordered by price ascending.</summary>
    public IReadOnlyList<StorePriceOffer> Offers { get; set; } = new List<StorePriceOffer>();
}
