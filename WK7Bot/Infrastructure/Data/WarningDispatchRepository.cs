using Microsoft.EntityFrameworkCore;
using WK7Bot.Core.Entities;
using WK7Bot.Core.Interfaces;

namespace WK7Bot.Infrastructure.Data;

/// <summary>
/// Entity Framework Core implementation of the DWD warning dispatch state data access contract.
/// </summary>
public class WarningDispatchRepository : IWarningDispatchRepository
{
    private readonly BotDbContext _dbContext;

    /// <summary>
    /// Initializes a new instance of the <see cref="WarningDispatchRepository"/> class.
    /// </summary>
    /// <param name="dbContext">The database context instance.</param>
    public WarningDispatchRepository(BotDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <summary>
    /// Determines whether the warning with the given identifier was already posted to a guild.
    /// </summary>
    /// <param name="warningId">The stable CAP warning identifier.</param>
    /// <param name="guildId">The target Discord guild ID.</param>
    /// <param name="cancellationToken">Cancellation token to observe.</param>
    /// <returns><see langword="true"/> when the warning was already posted; otherwise <see langword="false"/>.</returns>
    public async Task<bool> HasSentAsync(string warningId, ulong guildId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.WarningDispatchLogs
            .AsNoTracking()
            .AnyAsync(
                log => log.WarningId == warningId && log.GuildId == guildId,
                cancellationToken);
    }

    /// <summary>
    /// Records that the warning with the given identifier was posted to a guild. Idempotent.
    /// </summary>
    /// <param name="warningId">The stable CAP warning identifier.</param>
    /// <param name="guildId">The target Discord guild ID.</param>
    /// <param name="sentAt">The local time the message was sent.</param>
    /// <param name="cancellationToken">Cancellation token to observe.</param>
    /// <returns>A task representing the asynchronous storage operation.</returns>
    public async Task MarkSentAsync(string warningId, ulong guildId, DateTime sentAt, CancellationToken cancellationToken = default)
    {
        var alreadySent = await _dbContext.WarningDispatchLogs
            .AnyAsync(
                log => log.WarningId == warningId && log.GuildId == guildId,
                cancellationToken);

        if (alreadySent)
        {
            return;
        }

        var log = new WarningDispatchLog { WarningId = warningId, GuildId = guildId, SentAt = sentAt };
        await _dbContext.WarningDispatchLogs.AddAsync(log, cancellationToken);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (DatabaseWriteGuard.IsUniqueConstraintViolation(ex))
        {
            // Lost the race against a concurrent dispatcher — the row already exists, which is the
            // desired state. Detach the failed insert so the shared context stays usable.
            _dbContext.Entry(log).State = EntityState.Detached;
        }
    }

    /// <summary>
    /// Removes dispatch records older than the given cutoff so the table does not grow forever.
    /// </summary>
    /// <param name="cutoff">Records strictly older than this instant are deleted.</param>
    /// <param name="cancellationToken">Cancellation token to observe.</param>
    /// <returns>A task representing the asynchronous cleanup operation.</returns>
    public async Task PruneAsync(DateTime cutoff, CancellationToken cancellationToken = default)
    {
        var stale = await _dbContext.WarningDispatchLogs
            .Where(log => log.SentAt < cutoff)
            .ToListAsync(cancellationToken);

        if (stale.Count == 0)
        {
            return;
        }

        _dbContext.WarningDispatchLogs.RemoveRange(stale);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
