namespace WK7Bot.Infrastructure.Data;

using Microsoft.EntityFrameworkCore;
using WK7Bot.Core.Entities;
using WK7Bot.Core.Interfaces;

/// <summary>
/// SQLite-backed repository for user XP and level profiles.
/// </summary>
public class XpRepository : IXpRepository
{
    private readonly IDbContextFactory<BotDbContext> _dbContextFactory;

    /// <summary>Initializes a new instance of the <see cref="XpRepository"/> class.</summary>
    /// <param name="dbContextFactory">The database context factory.</param>
    public XpRepository(IDbContextFactory<BotDbContext> dbContextFactory)
    {
        _dbContextFactory = dbContextFactory ?? throw new ArgumentNullException(nameof(dbContextFactory));
    }

    /// <inheritdoc />
    public async Task<XpProfile?> GetByUserAsync(ulong discordUserId, ulong guildId, CancellationToken cancellationToken = default)
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await context.Set<XpProfile>()
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.DiscordUserId == discordUserId && p.GuildId == guildId, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<XpProfile> GetOrCreateAsync(ulong discordUserId, ulong guildId, CancellationToken cancellationToken = default)
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var profile = await context.Set<XpProfile>()
                .FirstOrDefaultAsync(p => p.DiscordUserId == discordUserId && p.GuildId == guildId, cancellationToken);

            if (profile is null)
            {
                profile = new XpProfile { DiscordUserId = discordUserId, GuildId = guildId };
                context.Set<XpProfile>().Add(profile);
                await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return profile;
        }
        catch (Exception)
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task UpsertAsync(XpProfile profile, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);

        await using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var existing = await context.Set<XpProfile>()
                .FirstOrDefaultAsync(p => p.DiscordUserId == profile.DiscordUserId && p.GuildId == profile.GuildId, cancellationToken);

            if (existing is null)
            {
                context.Set<XpProfile>().Add(profile);
            }
            else
            {
                existing.Xp = profile.Xp;
                existing.Level = profile.Level;
                existing.LastXpAt = profile.LastXpAt;
                existing.VoiceMinutes = profile.VoiceMinutes;
            }

            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception)
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    private async Task<XpProfile> EnsureProfileAsync(ulong discordUserId, ulong guildId, CancellationToken cancellationToken)
    {
        var existing = await GetByUserAsync(discordUserId, guildId, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var created = new XpProfile { DiscordUserId = discordUserId, GuildId = guildId };
        await using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        context.Set<XpProfile>().Add(created);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return created;
    }

    /// <inheritdoc />
    public async Task AddXpAsync(ulong discordUserId, ulong guildId, int amount, CancellationToken cancellationToken = default)
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var profile = await context.Set<XpProfile>()
                .FirstOrDefaultAsync(p => p.DiscordUserId == discordUserId && p.GuildId == guildId, cancellationToken);

            if (profile is null)
            {
                profile = new XpProfile { DiscordUserId = discordUserId, GuildId = guildId, Xp = amount, LastXpAt = DateTime.UtcNow };
                context.Set<XpProfile>().Add(profile);
            }
            else
            {
                profile.Xp += amount;
                profile.LastXpAt = DateTime.UtcNow;
            }

            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception)
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task SetLevelAsync(ulong discordUserId, ulong guildId, int level, CancellationToken cancellationToken = default)
    {
        var xp = level * level * 100;
        await SetXpAsync(discordUserId, guildId, xp, level, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task SetXpAsync(ulong discordUserId, ulong guildId, int xp, int level, CancellationToken cancellationToken = default)
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var profile = await context.Set<XpProfile>()
                .FirstOrDefaultAsync(p => p.DiscordUserId == discordUserId && p.GuildId == guildId, cancellationToken);

            if (profile is null)
            {
                profile = new XpProfile { DiscordUserId = discordUserId, GuildId = guildId, Xp = xp, Level = level, LastXpAt = DateTime.UtcNow };
                context.Set<XpProfile>().Add(profile);
            }
            else
            {
                profile.Xp = xp;
                profile.Level = level;
                profile.LastXpAt = DateTime.UtcNow;
            }

            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception)
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task ResetAsync(ulong discordUserId, ulong guildId, CancellationToken cancellationToken = default)
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var profile = await context.Set<XpProfile>()
            .FirstOrDefaultAsync(p => p.DiscordUserId == discordUserId && p.GuildId == guildId, cancellationToken);

        if (profile is null)
        {
            return;
        }

        profile.Xp = 0;
        profile.Level = 0;
        profile.LastXpAt = DateTime.MinValue;
        profile.VoiceMinutes = 0;
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<bool> RemoveAsync(ulong discordUserId, ulong guildId, CancellationToken cancellationToken = default)
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var profile = await context.Set<XpProfile>()
            .FirstOrDefaultAsync(p => p.DiscordUserId == discordUserId && p.GuildId == guildId, cancellationToken);

        if (profile is null)
        {
            return false;
        }

        context.Set<XpProfile>().Remove(profile);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<XpProfile>> GetLeaderboardAsync(ulong guildId, int limit, CancellationToken cancellationToken = default)
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await context.Set<XpProfile>()
            .AsNoTracking()
            .Where(p => p.GuildId == guildId)
            .OrderByDescending(p => p.Xp)
            .ThenBy(p => p.DiscordUserId)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<XpProfile>> GetVoiceLeaderboardAsync(ulong guildId, int limit, CancellationToken cancellationToken = default)
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await context.Set<XpProfile>()
            .AsNoTracking()
            .Where(p => p.GuildId == guildId)
            .OrderByDescending(p => p.VoiceMinutes)
            .ThenByDescending(p => p.Xp)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }
}
