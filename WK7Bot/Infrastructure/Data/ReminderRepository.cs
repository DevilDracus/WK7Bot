namespace WK7Bot.Infrastructure.Data;

using Microsoft.EntityFrameworkCore;
using WK7Bot.Core.Entities;
using WK7Bot.Core.Interfaces;

/// <summary>
/// SQLite-backed repository for personal reminders.
/// </summary>
public class ReminderRepository : IReminderRepository
{
    private readonly IDbContextFactory<BotDbContext> _dbContextFactory;

    /// <summary>Initializes a new instance of the <see cref="ReminderRepository"/> class.</summary>
    /// <param name="dbContextFactory">The database context factory.</param>
    public ReminderRepository(IDbContextFactory<BotDbContext> dbContextFactory)
    {
        _dbContextFactory = dbContextFactory ?? throw new ArgumentNullException(nameof(dbContextFactory));
    }

    /// <inheritdoc />
    public async Task AddAsync(Reminder reminder, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reminder);

        await using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        context.Reminders.Add(reminder);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Reminder>> GetDueAsync(DateTime nowUtc, CancellationToken cancellationToken = default)
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await context.Reminders
            .AsNoTracking()
            .Where(r => r.DeliveredAt == null && r.DueAt <= nowUtc)
            .OrderBy(r => r.DueAt)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Reminder>> GetPendingForUserAsync(ulong discordUserId, CancellationToken cancellationToken = default)
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await context.Reminders
            .AsNoTracking()
            .Where(r => r.DiscordUserId == discordUserId && r.DeliveredAt == null)
            .OrderBy(r => r.DueAt)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task MarkDeliveredAsync(int id, CancellationToken cancellationToken = default)
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var reminder = await context.Reminders.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (reminder is null)
        {
            return;
        }

        reminder.DeliveredAt = DateTime.UtcNow;
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task MarkDeliveredManyAsync(IEnumerable<int> ids, CancellationToken cancellationToken = default)
    {
        var idSet = ids.ToList();
        if (idSet.Count == 0)
        {
            return;
        }

        await using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var deliveredAt = DateTime.UtcNow;
        var reminders = await context.Reminders
            .Where(r => idSet.Contains(r.Id))
            .ToListAsync(cancellationToken);

        foreach (var reminder in reminders)
        {
            reminder.DeliveredAt = deliveredAt;
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task RescheduleAsync(int id, DateTime newDueAt, CancellationToken cancellationToken = default)
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var reminder = await context.Reminders.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (reminder is null)
        {
            return;
        }

        reminder.DueAt = newDueAt;
        reminder.DeliveredAt = null;
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task RescheduleManyAsync(IEnumerable<(int Id, DateTime NewDueAt)> updates, CancellationToken cancellationToken = default)
    {
        var updateList = updates.ToList();
        if (updateList.Count == 0)
        {
            return;
        }

        await using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var ids = updateList.Select(u => u.Id).ToList();
        var reminders = await context.Reminders
            .Where(r => ids.Contains(r.Id))
            .ToListAsync(cancellationToken);

        var updateById = updateList.ToDictionary(u => u.Id, u => u.NewDueAt);
        foreach (var reminder in reminders)
        {
            if (updateById.TryGetValue(reminder.Id, out var newDueAt))
            {
                reminder.DueAt = newDueAt;
                reminder.DeliveredAt = null;
            }
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
    /// <summary>
    /// Removes the pending reminder with the given identifier for the specified Discord user.
    /// </summary>
    /// <param name="id">The reminder identifier.</param>
    /// <param name="discordUserId">The Discord user identifier that owns the reminder.</param>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    /// <returns><see langword="true"/> when a pending reminder was removed; otherwise <see langword="false"/>.</returns>
    public async Task<bool> RemovePendingAsync(int id, ulong discordUserId, CancellationToken cancellationToken = default)
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var reminder = await context.Reminders
            .FirstOrDefaultAsync(r => r.Id == id && r.DiscordUserId == discordUserId && r.DeliveredAt == null, cancellationToken);

        if (reminder is null)
        {
            return false;
        }

        context.Reminders.Remove(reminder);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <inheritdoc />
    public async Task<int> PruneDeliveredAsync(DateTime olderThanUtc, CancellationToken cancellationToken = default)
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var stale = await context.Reminders
            .Where(r => r.DeliveredAt != null && r.DeliveredAt < olderThanUtc)
            .ToListAsync(cancellationToken);

        if (stale.Count == 0)
        {
            return 0;
        }

        context.Reminders.RemoveRange(stale);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return stale.Count;
    }
}
