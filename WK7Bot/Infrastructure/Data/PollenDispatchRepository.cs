namespace WK7Bot.Infrastructure.Data;

using Microsoft.EntityFrameworkCore;
using WK7Bot.Core.Entities;
using WK7Bot.Core.Interfaces;

/// <summary>
/// Entity Framework Core implementation of the pollen dispatch state data access contract.
/// </summary>
public class PollenDispatchRepository : IPollenDispatchRepository
{
    private readonly IDbContextFactory<BotDbContext> _dbContextFactory;

    /// <summary>
    /// Initializes a new instance of the <see cref="PollenDispatchRepository"/> class.
    /// </summary>
    /// <param name="dbContextFactory">The database context factory.</param>
    public PollenDispatchRepository(IDbContextFactory<BotDbContext> dbContextFactory)
    {
        _dbContextFactory = dbContextFactory ?? throw new ArgumentNullException(nameof(dbContextFactory));
    }

    /// <inheritdoc />
    public async Task<PollenDispatchState?> GetLastAsync(ulong guildId, CancellationToken cancellationToken = default)
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await context.PollenDispatchStates
            .AsNoTracking()
            .FirstOrDefaultAsync(state => state.GuildId == guildId, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task UpsertAsync(ulong guildId, DateTime lastPostedDate, string forecastJson, CancellationToken cancellationToken = default)
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var existing = await context.PollenDispatchStates
            .FirstOrDefaultAsync(state => state.GuildId == guildId, cancellationToken)
            .ConfigureAwait(false);

        if (existing is null)
        {
            await context.PollenDispatchStates.AddAsync(
                new PollenDispatchState
                {
                    GuildId = guildId,
                    LastPostedDate = lastPostedDate.Date,
                    ForecastJson = forecastJson ?? string.Empty
                },
                cancellationToken).ConfigureAwait(false);
        }
        else
        {
            existing.LastPostedDate = lastPostedDate.Date;
            existing.ForecastJson = forecastJson ?? string.Empty;
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}