namespace WK7Bot.Services;

using System.IO.Compression;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using WK7Bot.Infrastructure.Data;
using WK7Bot.Options;

/// <summary>
/// Background service that performs nightly SQLite backups and prunes old dispatch logs
/// to prevent unbounded database growth.
/// </summary>
public class DatabaseMaintenanceService : BackgroundService
{
    private static readonly TimeSpan RunInterval = TimeSpan.FromHours(24);
    private static readonly TimeSpan InitialDelay = TimeSpan.FromMinutes(5);

    private readonly IDbContextFactory<BotDbContext> _dbContextFactory;
    private readonly CommandUsageRepository _commandUsageRepository;
    private readonly Wk7BotOptions _options;
    private readonly ILogger<DatabaseMaintenanceService> _logger;

    /// <summary>
    /// Default retention for dispatch logs in days.
    /// </summary>
    public const int DefaultLogRetentionDays = 30;

    /// <summary>
    /// Default retention for command usage logs in days.
    /// </summary>
    public const int DefaultCommandUsageRetentionDays = 90;

    /// <summary>
    /// Default retention for backup files in days.
    /// </summary>
    public const int DefaultBackupRetentionDays = 7;

    /// <summary>
    /// Initializes a new instance of the <see cref="DatabaseMaintenanceService"/> class.
    /// </summary>
    public DatabaseMaintenanceService(
        IDbContextFactory<BotDbContext> dbContextFactory,
        CommandUsageRepository commandUsageRepository,
        IOptions<Wk7BotOptions> options,
        ILogger<DatabaseMaintenanceService> logger)
    {
        _dbContextFactory = dbContextFactory ?? throw new ArgumentNullException(nameof(dbContextFactory));
        _commandUsageRepository = commandUsageRepository ?? throw new ArgumentNullException(nameof(commandUsageRepository));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Runs the maintenance loop: backup first, then prune.
    /// </summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(InitialDelay, stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        using var timer = new PeriodicTimer(RunInterval);

        do
        {
            try
            {
                await RunMaintenanceAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Database maintenance cycle failed.");
            }
        }
        while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }

    /// <summary>
    /// Performs backup and prune in sequence.
    /// </summary>
    private async Task RunMaintenanceAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Starting database maintenance cycle.");

        await BackupDatabaseAsync(stoppingToken).ConfigureAwait(false);
        await PruneOldLogsAsync(stoppingToken).ConfigureAwait(false);

        _logger.LogInformation("Database maintenance cycle complete.");
    }

    /// <summary>
    /// Creates a compressed backup of the SQLite database file.
    /// </summary>
    private async Task BackupDatabaseAsync(CancellationToken stoppingToken)
    {
        var dbPath = await GetDatabasePathAsync().ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(dbPath) || !File.Exists(dbPath))
        {
            _logger.LogWarning("Database file not found at expected path; skipping backup.");
            return;
        }

        var backupDir = Path.Combine(Path.GetDirectoryName(dbPath) ?? "/data", "backups");
        Directory.CreateDirectory(backupDir);

        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        var backupName = $"wk7bot_backup_{timestamp}.db.gz";
        var backupPath = Path.Combine(backupDir, backupName);

        try
        {
            await using var sourceStream = File.OpenRead(dbPath);
            await using var targetStream = File.Create(backupPath);
            await using var gzipStream = new GZipStream(targetStream, CompressionLevel.Optimal);
            await sourceStream.CopyToAsync(gzipStream, stoppingToken).ConfigureAwait(false);

            _logger.LogInformation("Database backed up to {BackupPath}.", backupPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Database backup failed.");
            return;
        }

        // Prune old backups
        PruneOldBackups(backupDir);
    }

    /// <summary>
    /// Deletes backup files older than the retention period.
    /// </summary>
    private void PruneOldBackups(string backupDir)
    {
        var cutoff = DateTime.UtcNow.AddDays(-DefaultBackupRetentionDays);
        var deleted = 0;

        foreach (var file in Directory.EnumerateFiles(backupDir, "wk7bot_backup_*.db.gz"))
        {
            try
            {
                if (File.GetLastWriteTimeUtc(file) < cutoff)
                {
                    File.Delete(file);
                    deleted++;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not delete old backup {File}.", file);
            }
        }

        if (deleted > 0)
        {
            _logger.LogInformation("Pruned {Deleted} old database backup(s).", deleted);
        }
    }

    /// <summary>
    /// Prunes old dispatch and command usage logs.
    /// </summary>
    private async Task PruneOldLogsAsync(CancellationToken stoppingToken)
    {
        try
        {
            var commandPruned = await _commandUsageRepository.PruneOldLogsAsync(
                DefaultCommandUsageRetentionDays, stoppingToken).ConfigureAwait(false);

            if (commandPruned > 0)
            {
                _logger.LogInformation("Pruned {Count} old command usage log(s).", commandPruned);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to prune command usage logs.");
        }

        // Prune other dispatch logs via raw ADO.NET (EF Core 10 does not expose ExecuteSqlRawAsync on DatabaseFacade).
        try
        {
            var cutoff = DateTime.UtcNow.AddDays(-DefaultLogRetentionDays);
            var cutoffString = cutoff.ToString("yyyy-MM-dd HH:mm:ss.ffffff");

            await using var dbContext = await _dbContextFactory.CreateDbContextAsync(stoppingToken).ConfigureAwait(false);
            await using var connection = dbContext.Database.GetDbConnection();
            await connection.OpenAsync(stoppingToken).ConfigureAwait(false);

            int wastePruned = 0, warningPruned = 0, foodPruned = 0;

            await using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = "DELETE FROM WasteDispatchLogs WHERE SentOn < @cutoff;";
                var p = cmd.CreateParameter();
                p.ParameterName = "@cutoff";
                p.Value = cutoffString;
                cmd.Parameters.Add(p);
                wastePruned = await cmd.ExecuteNonQueryAsync(stoppingToken).ConfigureAwait(false);
            }

            await using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = "DELETE FROM WarningDispatchLogs WHERE SentAt < @cutoff;";
                var p = cmd.CreateParameter();
                p.ParameterName = "@cutoff";
                p.Value = cutoffString;
                cmd.Parameters.Add(p);
                warningPruned = await cmd.ExecuteNonQueryAsync(stoppingToken).ConfigureAwait(false);
            }

            await using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = "DELETE FROM FoodDispatchLogs WHERE SentOn < @cutoff;";
                var p = cmd.CreateParameter();
                p.ParameterName = "@cutoff";
                p.Value = cutoffString;
                cmd.Parameters.Add(p);
                foodPruned = await cmd.ExecuteNonQueryAsync(stoppingToken).ConfigureAwait(false);
            }

            var totalPruned = wastePruned + warningPruned + foodPruned;
            if (totalPruned > 0)
            {
                _logger.LogInformation(
                    "Pruned dispatch logs: {Waste} waste, {Warning} warning, {Food} food.",
                    wastePruned, warningPruned, foodPruned);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to prune dispatch logs.");
        }
    }

    /// <summary>
    /// Resolves the SQLite database file path from the connection string.
    /// </summary>
    private async Task<string?> GetDatabasePathAsync()
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync();
        var connectionString = dbContext.Database.GetConnectionString();
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return null;
        }

        var dataSource = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder(connectionString).DataSource;
        return string.IsNullOrWhiteSpace(dataSource) ? null : dataSource;
    }
}
