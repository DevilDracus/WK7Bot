namespace WK7Bot.Infrastructure.Data;

using Microsoft.EntityFrameworkCore;
using WK7Bot.Core.Entities;
using WK7Bot.Core.Interfaces;

/// <summary>
/// SQLite-backed repository for Steam achievement baselines.
/// </summary>
public class SteamAchievementBaselineRepository : ISteamAchievementBaselineRepository
{
    private readonly IDbContextFactory<BotDbContext> _dbContextFactory;

    /// <summary>Initializes a new instance of the <see cref="SteamAchievementBaselineRepository"/> class.</summary>
    /// <param name="dbContextFactory">The database context factory.</param>
    public SteamAchievementBaselineRepository(IDbContextFactory<BotDbContext> dbContextFactory)
    {
        _dbContextFactory = dbContextFactory ?? throw new ArgumentNullException(nameof(dbContextFactory));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SteamAchievementBaseline>> GetBaselineAsync(
        ulong? discordUserId = null,
        string? steamId = null,
        uint? appId = null,
        string? apiName = null,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var query = context.SteamAchievementBaselines.AsNoTracking();
        if (discordUserId.HasValue)
        {
            query = query.Where(b => b.DiscordUserId == discordUserId.Value);
        }
        if (steamId is not null)
        {
            query = query.Where(b => b.SteamId == steamId);
        }
        if (appId.HasValue)
        {
            query = query.Where(b => b.AppId == appId.Value);
        }
        if (apiName is not null)
        {
            query = query.Where(b => b.ApiName == apiName);
        }
        return await query.ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<int> UpsertManyAsync(IEnumerable<SteamAchievementBaseline> baselines, CancellationToken cancellationToken = default)
    {
        var list = baselines?.ToList() ?? [];
        if (list.Count == 0)
        {
            return 0;
        }

        await using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var updated = 0;
        foreach (var baseline in list)
        {
            var existing = await context.SteamAchievementBaselines
                .FirstOrDefaultAsync(b => b.DiscordUserId == baseline.DiscordUserId
                    && b.SteamId == baseline.SteamId
                    && b.AppId == baseline.AppId
                    && b.ApiName == baseline.ApiName, cancellationToken)
                .ConfigureAwait(false);

            if (existing is null)
            {
                context.SteamAchievementBaselines.Add(new SteamAchievementBaseline
                {
                    DiscordUserId = baseline.DiscordUserId,
                    SteamId = baseline.SteamId,
                    AppId = baseline.AppId,
                    ApiName = baseline.ApiName,
                    Achieved = baseline.Achieved,
                    UnlockTime = baseline.UnlockTime,
                    LastChecked = baseline.LastChecked
                });
            }
            else
            {
                existing.Achieved = baseline.Achieved;
                existing.UnlockTime = baseline.UnlockTime;
                existing.LastChecked = baseline.LastChecked;
            }

            updated++;
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return updated;
    }
}
