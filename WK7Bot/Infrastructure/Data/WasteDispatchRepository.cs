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
    public async Task MarkSentAsync(string kind, ulong guildId, DateTime sentOn, CancellationToken cancellationToken = default)
    {
        var normalizedDate = sentOn.Date;
        await MarkSentAsyncCore(
            log => log.Kind == kind && log.GuildId == guildId && log.SentOn == normalizedDate,
            () => new WasteDispatchLog { Kind = kind, GuildId = guildId, SentOn = normalizedDate },
            cancellationToken);
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
