namespace WK7Bot.Core.Interfaces;

using WK7Bot.Core.Entities;

/// <summary>
/// Repository contract for Discord user to external gaming account links.
/// </summary>
public interface IUserAccountLinkRepository
{
    /// <summary>Returns every stored account link.</summary>
    Task<IReadOnlyList<UserAccountLink>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns all links for one Discord user.</summary>
    Task<IReadOnlyList<UserAccountLink>> GetForUserAsync(ulong discordUserId, CancellationToken cancellationToken = default);

    /// <summary>Returns one link for a user and provider, or <see langword="null"/>.</summary>
    Task<UserAccountLink?> GetAsync(ulong discordUserId, string provider, CancellationToken cancellationToken = default);

    /// <summary>Inserts or replaces the link for the given user and provider.</summary>
    Task UpsertAsync(UserAccountLink link, CancellationToken cancellationToken = default);

    /// <summary>Removes the link for the given user and provider.</summary>
    Task<bool> RemoveAsync(ulong discordUserId, string provider, CancellationToken cancellationToken = default);
}
