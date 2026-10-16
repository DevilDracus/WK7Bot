using System;

namespace WK7Bot.Core.Utilities;

/// <summary>
/// Central inventory of recurring cooldown and short-lived cache lifetimes used by background services.
/// Values are intentionally identical to the previous per-service defaults; this class exists so timing
/// behaviour can be reviewed in one place instead of hunting across services.
/// </summary>
public static class ServiceCooldowns
{
    /// <summary>
    /// Gets the time-to-live for cached Steam presence/data lookups.
    /// </summary>
    public static readonly TimeSpan SteamPresenceData = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Gets the time-to-live for cached Battle.net presence/data lookups.
    /// </summary>
    public static readonly TimeSpan BattleNetPresenceData = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Gets how often mapped Discord presence is refreshed independently of Discord events.
    /// </summary>
    public static readonly TimeSpan PresenceRefresh = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Gets how long a failing/challenged primary recipe-search provider is skipped before retry.
    /// </summary>
    public static readonly TimeSpan PrimaryRecipeSearchProvider = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Gets how long the same wishlist game is suppressed before another deal alert may be sent.
    /// </summary>
    public static readonly TimeSpan WishlistDealAlert = TimeSpan.FromHours(24);
}
