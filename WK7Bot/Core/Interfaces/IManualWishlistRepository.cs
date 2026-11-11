namespace WK7Bot.Core.Interfaces;

using WK7Bot.Core.Entities;

/// <summary>
/// Repository contract for per-user manual wishlist AppID entries.
/// </summary>
public interface IManualWishlistRepository
{
    /// <summary>Returns every manual wishlist entry.</summary>
    Task<IReadOnlyList<ManualWishlistItem>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns the manual wishlist entries for one Discord user.</summary>
    Task<IReadOnlyList<ManualWishlistItem>> GetForUserAsync(ulong discordUserId, CancellationToken cancellationToken = default);

    /// <summary>Adds a new manual wishlist entry.</summary>
    Task AddAsync(ManualWishlistItem item, CancellationToken cancellationToken = default);

    /// <summary>Removes a manual wishlist entry by user and AppID.</summary>
    Task<bool> RemoveAsync(ulong discordUserId, uint appId, CancellationToken cancellationToken = default);
}
