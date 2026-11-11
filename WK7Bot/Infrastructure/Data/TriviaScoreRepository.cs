namespace WK7Bot.Infrastructure.Data;

using Microsoft.EntityFrameworkCore;
using WK7Bot.Core.Entities;
using WK7Bot.Core.Interfaces;

/// <summary>
/// SQLite-backed repository for trivia scores.
/// </summary>
public class TriviaScoreRepository : ITriviaScoreRepository
{
    private readonly IDbContextFactory<BotDbContext> _dbContextFactory;

    /// <summary>Initializes a new instance of the <see cref="TriviaScoreRepository"/> class.</summary>
    /// <param name="dbContextFactory">The database context factory.</param>
    public TriviaScoreRepository(IDbContextFactory<BotDbContext> dbContextFactory)
    {
        _dbContextFactory = dbContextFactory ?? throw new ArgumentNullException(nameof(dbContextFactory));
    }

    /// <inheritdoc />
    public async Task IncrementAsync(ulong discordUserId, ulong guildId, DateTime weekStart, int points, CancellationToken cancellationToken = default)
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var score = await context.Set<TriviaScore>()
            .FirstOrDefaultAsync(s => s.DiscordUserId == discordUserId && s.GuildId == guildId && s.WeekStart == weekStart, cancellationToken);

        if (score is null)
        {
            context.Set<TriviaScore>().Add(new TriviaScore
            {
                DiscordUserId = discordUserId,
                GuildId = guildId,
                WeekStart = weekStart,
                Score = points,
                LastPlayedAt = DateTime.UtcNow
            });
        }
        else
        {
            score.Score += points;
            score.LastPlayedAt = DateTime.UtcNow;
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<TriviaScore>> GetTopAsync(ulong guildId, DateTime weekStart, int limit, CancellationToken cancellationToken = default)
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await context.Set<TriviaScore>()
            .AsNoTracking()
            .Where(s => s.GuildId == guildId && s.WeekStart == weekStart)
            .OrderByDescending(s => s.Score)
            .ThenBy(s => s.LastPlayedAt)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<int> PruneOlderThanAsync(DateTime olderThanWeekStart, CancellationToken cancellationToken = default)
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var stale = await context.Set<TriviaScore>()
            .Where(s => s.WeekStart < olderThanWeekStart)
            .ToListAsync(cancellationToken);

        if (stale.Count == 0)
        {
            return 0;
        }

        context.Set<TriviaScore>().RemoveRange(stale);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return stale.Count;
    }
}
