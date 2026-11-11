namespace WK7Bot.Infrastructure.Data;

using Microsoft.EntityFrameworkCore;
using WK7Bot.Core.Entities;
using WK7Bot.Core.Interfaces;

/// <summary>
/// SQLite-backed repository for per-user game release-date reminders.
/// </summary>
public class ReleaseDateReminderRepository : IReleaseDateReminderRepository
{
    private readonly IDbContextFactory<BotDbContext> _dbContextFactory;

    /// <summary>Initializes a new instance of the <see cref="ReleaseDateReminderRepository"/> class.</summary>
    /// <param name="dbContextFactory">The database context factory.</param>
    public ReleaseDateReminderRepository(IDbContextFactory<BotDbContext> dbContextFactory)
    {
        _dbContextFactory = dbContextFactory ?? throw new ArgumentNullException(nameof(dbContextFactory));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ReleaseDateReminderItem>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await context.ReleaseDateReminderItems.AsNoTracking().ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ReleaseDateReminderItem>> GetForUserAsync(ulong discordUserId, CancellationToken cancellationToken = default)
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await context.ReleaseDateReminderItems
            .AsNoTracking()
            .Where(r => r.DiscordUserId == discordUserId)
            .OrderBy(r => r.Name)
            .ThenBy(r => r.AppId)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task AddAsync(ReleaseDateReminderItem item, CancellationToken cancellationToken = default)
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        context.ReleaseDateReminderItems.Add(item);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<bool> RemoveAsync(ulong discordUserId, uint appId, CancellationToken cancellationToken = default)
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var existing = await context.ReleaseDateReminderItems
            .FirstOrDefaultAsync(r => r.DiscordUserId == discordUserId && r.AppId == appId, cancellationToken);
        if (existing is null)
        {
            return false;
        }

        context.ReleaseDateReminderItems.Remove(existing);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <inheritdoc />
    public async Task UpdateAsync(ReleaseDateReminderItem item, CancellationToken cancellationToken = default)
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        context.ReleaseDateReminderItems.Update(item);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
