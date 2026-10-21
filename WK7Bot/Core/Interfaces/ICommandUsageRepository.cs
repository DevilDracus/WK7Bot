namespace WK7Bot.Core.Interfaces;

using WK7Bot.Core.Entities;

/// <summary>
/// Repository for slash command usage metrics.
/// </summary>
public interface ICommandUsageRepository
{
    /// <summary>Records a command invocation.</summary>
    Task LogCommandAsync(
        string commandName,
        ulong userId,
        ulong guildId,
        bool succeeded = true,
        CancellationToken cancellationToken = default);

    /// <summary>Returns the most frequently used commands in the last N days.</summary>
    Task<IReadOnlyList<(string CommandName, int Count)>> GetTopCommandsAsync(
        int days,
        int top,
        CancellationToken cancellationToken = default);

    /// <summary>Prunes command usage logs older than the specified age.</summary>
    Task<int> PruneOldLogsAsync(int retentionDays, CancellationToken cancellationToken = default);
}
