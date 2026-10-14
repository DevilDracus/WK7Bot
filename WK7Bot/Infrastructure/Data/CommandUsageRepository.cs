namespace WK7Bot.Infrastructure.Data;

using Microsoft.EntityFrameworkCore;
using WK7Bot.Core.Entities;

/// <summary>
/// Repository for slash command usage metrics.
/// </summary>
public class CommandUsageRepository
{
    private readonly BotDbContext _dbContext;

    /// <summary>
    /// Initializes a new instance of the <see cref="CommandUsageRepository"/> class.
    /// </summary>
    /// <param name="dbContext">The database context.</param>
    public CommandUsageRepository(BotDbContext dbContext)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    }

    /// <summary>
    /// Records a command invocation.
    /// </summary>
    /// <param name="commandName">The command name.</param>
    /// <param name="userId">The Discord user ID.</param>
    /// <param name="guildId">The Discord guild ID.</param>
    /// <param name="succeeded">Whether the command succeeded.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task LogCommandAsync(
        string commandName,
        ulong userId,
        ulong guildId,
        bool succeeded = true,
        CancellationToken cancellationToken = default)
    {
        var entry = new CommandUsageLog
        {
            CommandName = commandName,
            UserId = userId,
            GuildId = guildId,
            InvokedAt = DateTimeOffset.UtcNow,
            Succeeded = succeeded
        };

        _dbContext.CommandUsageLogs.Add(entry);
        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Returns the most frequently used commands in the last N days.
    /// </summary>
    /// <param name="days">The lookback window in days.</param>
    /// <param name="top">The maximum number of results.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A list of (command name, invocation count) tuples.</returns>
    public async Task<IReadOnlyList<(string CommandName, int Count)>> GetTopCommandsAsync(
        int days,
        int top,
        CancellationToken cancellationToken)
    {
        var cutoff = DateTimeOffset.UtcNow.AddDays(-days);

        // Use an anonymous type for the EF Core projection — ValueTuple constructor
        // calls cannot be translated to SQL and throw InvalidOperationException at runtime.
        var query = await _dbContext.CommandUsageLogs
            .Where(e => e.InvokedAt >= cutoff)
            .GroupBy(e => e.CommandName)
            .Select(g => new { CommandName = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .Take(top)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return query.Select(x => (x.CommandName, x.Count)).ToList();
    }

    /// <summary>
    /// Prunes command usage logs older than the specified age.
    /// </summary>
    public async Task<int> PruneOldLogsAsync(int retentionDays, CancellationToken cancellationToken)
    {
        var cutoff = DateTimeOffset.UtcNow.AddDays(-retentionDays);
        var oldEntries = await _dbContext.CommandUsageLogs
            .Where(e => e.InvokedAt < cutoff)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (oldEntries.Count == 0)
        {
            return 0;
        }

        _dbContext.CommandUsageLogs.RemoveRange(oldEntries);
        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return oldEntries.Count;
    }
}
