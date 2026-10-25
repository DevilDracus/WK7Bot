using Microsoft.EntityFrameworkCore;
using WK7Bot.Core.Entities;
using WK7Bot.Core.Interfaces;

namespace WK7Bot.Infrastructure.Data;

/// <summary>
/// Entity Framework Core implementation of the DWD warning dispatch state data access contract.
/// </summary>
public class WarningDispatchRepository : DispatchRepositoryBase<WarningDispatchLog>, IWarningDispatchRepository
{
    /// <summary>
    /// Initializes a new instance of the <see cref="WarningDispatchRepository" /> class.
    /// </summary>
    /// <param name="dbContextFactory">The database context factory.</param>
    public WarningDispatchRepository(IDbContextFactory<BotDbContext> dbContextFactory)
        : base(dbContextFactory)
    {
    }

    /// <inheritdoc />
    protected override DbSet<WarningDispatchLog> GetDbSet(BotDbContext context) => context.WarningDispatchLogs;

    /// <inheritdoc />
    public async Task<bool> HasSentAsync(string warningId, ulong guildId, CancellationToken cancellationToken = default)
    {
        return await HasSentAsyncCore(
            log => log.WarningId == warningId && log.GuildId == guildId,
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task MarkSentAsync(string warningId, ulong guildId, DateTime sentAt, CancellationToken cancellationToken = default)
    {
        await MarkSentAsyncCore(
            log => log.WarningId == warningId && log.GuildId == guildId,
            () => new WarningDispatchLog { WarningId = warningId, GuildId = guildId, SentAt = sentAt },
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task PruneAsync(DateTime cutoff, CancellationToken cancellationToken = default)
    {
        await PruneAsyncCore(
            log => log.SentAt < cutoff,
            cancellationToken);
    }
}
