namespace WK7Bot.Services.Interfaces;

using WK7Bot.Models;

/// <summary>
/// Fetches Steam wishlists for mapped users and evaluates IsThereAnyDeal price data to produce actionable deal alerts.
/// </summary>
public interface IWishlistDealService
{
    /// <summary>
    /// Fetches the public Steam wishlist for a single user and returns the items found.
    /// </summary>
    /// <param name="steamId">The 64-bit Steam ID of the user.</param>
    /// <param name="wishlistUrl">An optional shared wishlist URL that may bypass profile privacy.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>The wishlist items, or an empty list when the wishlist is private or unavailable.</returns>
    Task<IReadOnlyList<SteamWishlistItem>> FetchWishlistAsync(string steamId, string? wishlistUrl = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Queries IsThereAnyDeal for current price data on a collection of Steam app IDs.
    /// </summary>
    /// <param name="appIds">The Steam application identifiers to look up.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A dictionary mapping app ID to price data.</returns>
    Task<IReadOnlyDictionary<uint, ItadPriceData>> FetchPricesAsync(IEnumerable<uint> appIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// Evaluates a single game's price data against the alert thresholds and returns an alert if warranted.
    /// </summary>
    /// <param name="appId">The Steam application identifier.</param>
    /// <param name="gameTitle">The display name of the game.</param>
    /// <param name="priceData">The current price data from IsThereAnyDeal.</param>
    /// <param name="notifiedUserIds">The Discord user IDs who have this game wishlisted.</param>
    /// <returns>A deal alert when thresholds are met; otherwise null.</returns>
    WishlistDealAlert? EvaluateAlert(uint appId, string gameTitle, ItadPriceData priceData, IReadOnlyList<ulong> notifiedUserIds);
}
