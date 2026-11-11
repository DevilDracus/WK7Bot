namespace WK7Bot.Infrastructure.Data;

using Microsoft.EntityFrameworkCore;
using WK7Bot.Core.Entities;
using WK7Bot.Core.Interfaces;

/// <summary>
/// SQLite-backed repository for voice-channel session tracking.
/// </summary>
public class VoiceStatsRepository : IVoiceStatsRepository
{
    private readonly IDbContextFactory<BotDbContext> _dbContextFactory;

    /// <summary>Initializes a new instance of the <see cref="VoiceStatsRepository"/> class.</summary>
    /// <param name="dbContextFactory">The database context factory.</param>
    public VoiceStatsRepository(IDbContextFactory<BotDbContext> dbContextFactory)
    {
        _dbContextFactory = dbContextFactory ?? throw new ArgumentNullException(nameof(dbContextFactory));
    }

    /// <inheritdoc />
    public async Task AddAsync(VoiceSession session, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);

        await using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        context.Set<VoiceSession>().Add(session);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<int> GetTotalSecondsAsync(ulong discordUserId, ulong guildId, CancellationToken cancellationToken = default)
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await context.Set<VoiceSession>()
            .AsNoTracking()
            .Where(s => s.DiscordUserId == discordUserId && s.GuildId == guildId)
            .SumAsync(s => (int?)s.DurationSeconds, cancellationToken) ?? 0;
    }
}
