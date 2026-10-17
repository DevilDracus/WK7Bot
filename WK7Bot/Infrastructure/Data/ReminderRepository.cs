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
        await context.SaveChangesAsync(cancellationToken);
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
        await context.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
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
        await context.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <inheritdoc />
    public async Task<int> PruneDeliveredAsync(DateTime olderThanUtc, CancellationToken cancellationToken = default)
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await context.Reminders
            .Where(r => r.DeliveredAt != null && r.DeliveredAt < olderThanUtc)
            .ExecuteDeleteAsync(cancellationToken);
    }
}
