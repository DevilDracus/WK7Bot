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

    /// <inheritdoc />
    public async Task UpdateManyAsync(IEnumerable<ReleaseDateReminderItem> items, CancellationToken cancellationToken = default)
    {
        var itemList = items.ToList();
        if (itemList.Count == 0)
        {
            return;
        }

        await using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var ids = itemList.Select(i => i.Id).ToList();
        var existing = await context.ReleaseDateReminderItems
            .Where(r => ids.Contains(r.Id))
            .ToListAsync(cancellationToken);

        var itemById = itemList.ToDictionary(i => i.Id);
        foreach (var reminder in existing)
        {
            if (itemById.TryGetValue(reminder.Id, out var updated))
            {
                reminder.Name = updated.Name;
                reminder.ReleaseDate = updated.ReleaseDate;
                reminder.IsTba = updated.IsTba;
                reminder.LastNotifiedAt = updated.LastNotifiedAt;
            }
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
