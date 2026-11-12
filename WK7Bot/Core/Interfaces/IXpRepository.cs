namespace WK7Bot.Core.Interfaces;

using WK7Bot.Core.Entities;

/// <summary>
/// Data access for user XP and level profiles.
/// </summary>
public interface IXpRepository
{
    /// <summary>Returns the XP profile for a user in a guild, if any.</summary>
    Task<XpProfile?> GetAsync(ulong discordUserId, ulong guildId, CancellationToken cancellationToken = default);

    /// <summary>Returns the XP profile for a user in a guild, if any.</summary>
    Task<XpProfile?> GetByUserAsync(ulong discordUserId, ulong guildId, CancellationToken cancellationToken = default);

    /// <summary>Returns the XP profile for a user in a guild, if any.</summary>
    Task<XpProfile?> GetProfileAsync(ulong discordUserId, ulong guildId, CancellationToken cancellationToken = default);

    /// <summary>Returns the XP profile for a user in a guild, creating a default one if missing.</summary>
    Task<XpProfile> GetOrCreateAsync(ulong discordUserId, ulong guildId, CancellationToken cancellationToken = default);

    /// <summary>Returns the XP profile for a user in a guild, creating a default one if missing.</summary>
    Task<XpProfile> GetOrCreateProfileAsync(ulong discordUserId, ulong guildId, CancellationToken cancellationToken = default);

    /// <summary>Stores or updates an XP profile.</summary>
    Task UpsertAsync(XpProfile profile, CancellationToken cancellationToken = default);

    /// <summary>Stores or updates an XP profile.</summary>
    Task UpdateAsync(XpProfile profile, CancellationToken cancellationToken = default);

    /// <summary>Adds XP to a user's profile, creating it if needed.</summary>
    Task AddXpAsync(ulong discordUserId, ulong guildId, int amount, CancellationToken cancellationToken = default);

    /// <summary>Adds XP to a user's profile, creating it if needed.</summary>
    Task IncrementAsync(ulong discordUserId, ulong guildId, int amount, CancellationToken cancellationToken = default);

    /// <summary>Adds XP to a user's profile, creating it if needed.</summary>
    Task IncrementXpAsync(ulong discordUserId, ulong guildId, int amount, CancellationToken cancellationToken = default);

    /// <summary>Sets a user's level (and matching XP) in a guild.</summary>
    Task SetLevelAsync(ulong discordUserId, ulong guildId, int level, CancellationToken cancellationToken = default);

    /// <summary>Sets a user's XP and level in a guild.</summary>
    Task SetXpAsync(ulong discordUserId, ulong guildId, int xp, int level, CancellationToken cancellationToken = default);

    /// <summary>Resets a user's XP and level in a guild.</summary>
    Task ResetAsync(ulong discordUserId, ulong guildId, CancellationToken cancellationToken = default);

    /// <summary>Removes a user's XP profile from a guild.</summary>
    Task<bool> RemoveAsync(ulong discordUserId, ulong guildId, CancellationToken cancellationToken = default);

    /// <summary>Returns the top XP profiles for a guild.</summary>
    Task<IReadOnlyList<XpProfile>> GetTopAsync(ulong guildId, int limit, CancellationToken cancellationToken = default);

    /// <summary>Returns the top XP profiles for a guild.</summary>
    Task<IReadOnlyList<XpProfile>> GetLeaderboardAsync(ulong guildId, int limit, CancellationToken cancellationToken = default);

    /// <summary>Returns the top XP profiles for a guild ordered by voice minutes.</summary>
    Task<IReadOnlyList<XpProfile>> GetTopByVoiceAsync(ulong guildId, int limit, CancellationToken cancellationToken = default);

    /// <summary>Returns the top XP profiles for a guild ordered by voice minutes.</summary>
    Task<IReadOnlyList<XpProfile>> GetVoiceLeaderboardAsync(ulong guildId, int limit, CancellationToken cancellationToken = default);
}
