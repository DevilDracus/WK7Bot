using Microsoft.EntityFrameworkCore;
using WK7Bot.Core.Entities;
using WK7Bot.Core.Interfaces;

namespace WK7Bot.Infrastructure.Data;

/// <summary>
/// Entity Framework Core implementation of the waste dispatch state data access contract.
/// </summary>
public class WasteDispatchRepository : DispatchRepositoryBase<WasteDispatchLog>, IWasteDispatchRepository
{
    /// <summary>
    /// Initializes a new instance of the <see cref="WasteDispatchRepository" /> class.
    /// </summary>
    /// <param name="dbContextFactory">The database context factory.</param>
    public WasteDispatchRepository(IDbContextFactory<BotDbContext> dbContextFactory)
        : base(dbContextFactory)
    {
    }

    /// <inheritdoc />
    protected override DbSet<WasteDispatchLog> GetDbSet(BotDbContext context) => context.WasteDispatchLogs;

    /// <inheritdoc />
    public async Task<bool> HasSentAsync(string kind, ulong guildId, DateTime sentOn, CancellationToken cancellationToken = default)
    {
        var normalizedDate = sentOn.Date;
        return await HasSentAsyncCore(
            log => log.Kind == kind && log.GuildId == guildId && log.SentOn == normalizedDate,
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ulong>> FilterNotSentAsync(IEnumerable<ulong> guildIds, string kind, DateTime sentOn, CancellationToken cancellationToken = default)
    {
        var guildIdSet = guildIds.ToList();
        if (guildIdSet.Count == 0)
        {
            return [];
        }

        var normalizedDate = sentOn.Date;
        await using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var sent = await GetDbSet(context)
            .AsNoTracking()
            .Where(log => log.Kind == kind && log.SentOn == normalizedDate && guildIdSet.Contains(log.GuildId))
            .Select(log => log.GuildId)
            .ToListAsync(cancellationToken);

        var sentSet = new HashSet<ulong>(sent);
        return guildIdSet.Where(id => !sentSet.Contains(id)).ToList();
    }

    /// <inheritdoc />
    public async Task MarkSentAsync(string kind, ulong guildId, DateTime sentOn, CancellationToken cancellationToken = default)
    {
        var normalizedDate = sentOn.Date;
        await MarkSentAsyncCore(
            log => log.Kind == kind && log.GuildId == guildId && log.SentOn == normalizedDate,
            () => new WasteDispatchLog { Kind = kind, GuildId = guildId, SentOn = normalizedDate },
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task MarkSentManyAsync(IEnumerable<ulong> guildIds, string kind, DateTime sentOn, CancellationToken cancellationToken = default)
    {
        var guildIdSet = guildIds.ToList();
        if (guildIdSet.Count == 0)
        {
            return;
        }

        var normalizedDate = sentOn.Date;
        await using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var sent = await GetDbSet(context)
            .AsNoTracking()
            .Where(log => log.Kind == kind && log.SentOn == normalizedDate && guildIdSet.Contains(log.GuildId))
            .Select(log => log.GuildId)
            .ToListAsync(cancellationToken);

        var sentSet = new HashSet<ulong>(sent);
        var toInsert = guildIdSet
            .Where(id => !sentSet.Contains(id))
            .Select(id => new WasteDispatchLog { Kind = kind, GuildId = id, SentOn = normalizedDate })
            .ToList();

        if (toInsert.Count == 0)
        {
            return;
        }

        await GetDbSet(context).AddRangeAsync(toInsert, cancellationToken).ConfigureAwait(false);
        try
        {
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException ex) when (DatabaseWriteGuard.IsUniqueConstraintViolation(ex))
        {
            foreach (var log in toInsert)
            {
                context.Entry(log).State = EntityState.Detached;
            }
        }
    }

    /// <inheritdoc />
    public async Task<int> PruneAsync(DateTime olderThan, CancellationToken cancellationToken = default)
    {
        var cutoff = olderThan.Date;
        return await PruneAsyncCore(
            log => log.SentOn < cutoff,
            cancellationToken);
    }
}
