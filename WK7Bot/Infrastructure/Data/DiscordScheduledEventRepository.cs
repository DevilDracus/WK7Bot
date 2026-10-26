namespace WK7Bot.Infrastructure.Data;

using Microsoft.EntityFrameworkCore;
using WK7Bot.Core.Entities;
using WK7Bot.Core.Interfaces;

/// <summary>
/// SQLite-backed repository for Discord guild scheduled events created by the bot.
/// </summary>
public class DiscordScheduledEventRepository : IDiscordScheduledEventRepository
{
    private readonly IDbContextFactory<BotDbContext> _dbContextFactory;

    /// <summary>Initializes a new instance of the <see cref="DiscordScheduledEventRepository"/> class.</summary>
    /// <param name="dbContextFactory">The database context factory.</param>
    public DiscordScheduledEventRepository(IDbContextFactory<BotDbContext> dbContextFactory)
    {
        _dbContextFactory = dbContextFactory ?? throw new ArgumentNullException(nameof(dbContextFactory));
    }

    /// <inheritdoc />
    public async Task AddAsync(DiscordScheduledEvent item, CancellationToken cancellationToken = default)
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        context.DiscordScheduledEvents.Add(item);
        await context.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DiscordScheduledEvent>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await context.DiscordScheduledEvents.AsNoTracking().ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DiscordScheduledEvent>> GetExpiredAsync(DateTime now, CancellationToken cancellationToken = default)
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await context.DiscordScheduledEvents
            .AsNoTracking()
            .Where(e => e.EndAt <= now)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task RemoveAsync(int id, CancellationToken cancellationToken = default)
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var existing = await context.DiscordScheduledEvents.FirstOrDefaultAsync(e => e.Id == id, cancellationToken);
        if (existing is null)
        {
            return;
        }

        context.DiscordScheduledEvents.Remove(existing);
        await context.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task RemoveRangeAsync(IEnumerable<int> ids, CancellationToken cancellationToken = default)
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var idSet = ids.ToList();
        if (idSet.Count == 0)
        {
            return;
        }

        var existing = await context.DiscordScheduledEvents
            .Where(e => idSet.Contains(e.Id))
            .ToListAsync(cancellationToken);
        context.DiscordScheduledEvents.RemoveRange(existing);
        await context.SaveChangesAsync(cancellationToken);
    }
}
