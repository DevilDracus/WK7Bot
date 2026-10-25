using Microsoft.EntityFrameworkCore;
using WK7Bot.Core.Entities;
using WK7Bot.Core.Interfaces;

namespace WK7Bot.Infrastructure.Data;

/// <summary>
/// Entity Framework Core implementation of the food dispatch state data access contract.
/// </summary>
public class FoodDispatchRepository : DispatchRepositoryBase<FoodDispatchLog>, IFoodDispatchRepository
{
    /// <summary>
    /// Initializes a new instance of the <see cref="FoodDispatchRepository" /> class.
    /// </summary>
    /// <param name="dbContextFactory">The database context factory.</param>
    public FoodDispatchRepository(IDbContextFactory<BotDbContext> dbContextFactory)
        : base(dbContextFactory)
    {
    }

    /// <inheritdoc />
    protected override DbSet<FoodDispatchLog> GetDbSet(BotDbContext context) => context.FoodDispatchLogs;

    /// <inheritdoc />
    public async Task<bool> HasSentAsync(string kind, DateTime sentOn, CancellationToken cancellationToken = default)
    {
        var normalizedDate = sentOn.Date;
        return await HasSentAsyncCore(
            log => log.Kind == kind && log.SentOn == normalizedDate,
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task MarkSentAsync(string kind, DateTime sentOn, CancellationToken cancellationToken = default)
    {
        var normalizedDate = sentOn.Date;
        await MarkSentAsyncCore(
            log => log.Kind == kind && log.SentOn == normalizedDate,
            () => new FoodDispatchLog { Kind = kind, SentOn = normalizedDate },
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
