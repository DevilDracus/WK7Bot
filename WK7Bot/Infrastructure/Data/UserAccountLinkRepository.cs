namespace WK7Bot.Infrastructure.Data;

using Microsoft.EntityFrameworkCore;
using WK7Bot.Core.Entities;
using WK7Bot.Core.Interfaces;

/// <summary>
/// SQLite-backed repository for Discord user to external account links.
/// </summary>
public class UserAccountLinkRepository : IUserAccountLinkRepository
{
    private readonly IDbContextFactory<BotDbContext> _dbContextFactory;

    /// <summary>Initializes a new instance of the <see cref="UserAccountLinkRepository"/> class.</summary>
    /// <param name="dbContextFactory">The database context factory.</param>
    public UserAccountLinkRepository(IDbContextFactory<BotDbContext> dbContextFactory)
    {
        _dbContextFactory = dbContextFactory ?? throw new ArgumentNullException(nameof(dbContextFactory));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<UserAccountLink>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await context.UserAccountLinks.AsNoTracking().ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<UserAccountLink>> GetForUserAsync(ulong discordUserId, CancellationToken cancellationToken = default)
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await context.UserAccountLinks
            .AsNoTracking()
            .Where(l => l.DiscordUserId == discordUserId)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<UserAccountLink?> GetAsync(ulong discordUserId, string provider, CancellationToken cancellationToken = default)
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await context.UserAccountLinks
            .AsNoTracking()
            .FirstOrDefaultAsync(l => l.DiscordUserId == discordUserId && l.Provider == provider, cancellationToken);
    }

    /// <inheritdoc />
    public async Task UpsertAsync(UserAccountLink link, CancellationToken cancellationToken = default)
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var existing = await context.UserAccountLinks
            .FirstOrDefaultAsync(l => l.DiscordUserId == link.DiscordUserId && l.Provider == link.Provider, cancellationToken);

        if (existing is null)
        {
            context.UserAccountLinks.Add(link);
        }
        else
        {
            existing.ExternalId = link.ExternalId;
            existing.DisplayName = link.DisplayName;
            existing.RefreshToken = link.RefreshToken;
            existing.Region = link.Region;
            existing.WishlistUrl = link.WishlistUrl;
            existing.LinkedAt = link.LinkedAt;
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<bool> RemoveAsync(ulong discordUserId, string provider, CancellationToken cancellationToken = default)
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var existing = await context.UserAccountLinks
            .FirstOrDefaultAsync(l => l.DiscordUserId == discordUserId && l.Provider == provider, cancellationToken);
        if (existing is null)
        {
            return false;
        }

        context.UserAccountLinks.Remove(existing);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }
}
