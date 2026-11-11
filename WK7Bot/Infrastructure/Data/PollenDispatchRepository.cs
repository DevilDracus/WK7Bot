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
    public async Task<IReadOnlyDictionary<ulong, PollenDispatchState?>> GetLastForGuildsAsync(IEnumerable<ulong> guildIds, CancellationToken cancellationToken = default)
    {
        var guildIdSet = guildIds.ToList();
        if (guildIdSet.Count == 0)
        {
            return new Dictionary<ulong, PollenDispatchState?>();
        }

        await using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var states = await context.PollenDispatchStates
            .AsNoTracking()
            .Where(state => guildIdSet.Contains(state.GuildId))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var stateByGuildId = states.ToDictionary(state => state.GuildId, state => (PollenDispatchState?)state);
        var result = new Dictionary<ulong, PollenDispatchState?>();
        foreach (var guildId in guildIdSet)
        {
            result[guildId] = stateByGuildId.TryGetValue(guildId, out var state) ? state : null;
        }

        return result;
    }

    /// <inheritdoc />
    public async Task UpsertAsync(ulong guildId, DateTime lastPostedDate, string forecastJson, CancellationToken cancellationToken = default)
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        try
        {
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
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception)
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task UpsertManyAsync(IEnumerable<(ulong GuildId, DateTime LastPostedDate, string ForecastJson)> entries, CancellationToken cancellationToken = default)
    {
        var entryList = entries.ToList();
        if (entryList.Count == 0)
        {
            return;
        }

        await using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var guildIds = entryList.Select(e => e.GuildId).ToList();
            var existing = await context.PollenDispatchStates
                .Where(state => guildIds.Contains(state.GuildId))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var existingByGuildId = existing.ToDictionary(state => state.GuildId);
            foreach (var (guildId, lastPostedDate, forecastJson) in entryList)
            {
                if (existingByGuildId.TryGetValue(guildId, out var state))
                {
                    state.LastPostedDate = lastPostedDate.Date;
                    state.ForecastJson = forecastJson ?? string.Empty;
                }
                else
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
            }

            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception)
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            throw;
        }
    }
}
