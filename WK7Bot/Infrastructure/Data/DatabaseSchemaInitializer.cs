using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using WK7Bot.Infrastructure.Data;

namespace WK7Bot.Infrastructure.Data;

/// <summary>
/// Ensures that the SQLite database file and schema are fully initialized on application startup.
/// Handles both brand-new databases (via EF Core <c>EnsureCreated</c>) and existing deployments
/// that need late-added tables and columns.
/// </summary>
public static class DatabaseSchemaInitializer
{
    /// <summary>
    /// Ensures that the parent directory exists and that the SQLite database file and schema are fully initialized on application startup.
    /// </summary>
    /// <param name="app">The running web application instance providing service resolution access.</param>
    /// <param name="cancellationToken">A cancellation token to observe during initialization.</param>
    /// <returns>A task representing the asynchronous database startup operation.</returns>
    public static async Task InitializeDatabaseAsync(this WebApplication app, CancellationToken cancellationToken = default)
    {
        using var scope = app.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BotDbContext>();

        var connectionString = dbContext.Database.GetConnectionString();
        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            var builder = new SqliteConnectionStringBuilder(connectionString);
            var dataSource = builder.DataSource;

            if (!string.IsNullOrWhiteSpace(dataSource) && dataSource != ":memory:")
            {
                var directoryPath = Path.GetDirectoryName(dataSource);
                if (!string.IsNullOrEmpty(directoryPath) && !Directory.Exists(directoryPath))
                {
                    Directory.CreateDirectory(directoryPath);
                }
            }
        }

        await dbContext.Database.EnsureCreatedAsync(cancellationToken).ConfigureAwait(false);

        // Everything below runs on a raw ADO connection on purpose: EF Core logs every failed
        // DbCommand at Error level — which feeds the admin error-DM queue — before any catch can
        // swallow it, and SQLite has no ADD COLUMN IF NOT EXISTS, so "duplicate column name" is an
        // expected outcome of these statements. The raw connection also lets the statements ride
        // out "database is locked" while a previous process is still shutting down.
        await using var connection = new SqliteConnection(dbContext.Database.GetConnectionString());
        await connection.OpenAsync().ConfigureAwait(false);

        // WAL lets the background services read while a writer commits instead of failing with
        // "database is locked"; busy_timeout makes writers wait for a competing lock instead of
        // erroring immediately. Both statements are harmless for in-memory databases.
        await ExecuteWithBusyRetryAsync(connection, "PRAGMA busy_timeout=5000;", cancellationToken).ConfigureAwait(false);
        await ExecuteWithBusyRetryAsync(connection, "PRAGMA journal_mode=WAL;", cancellationToken).ConfigureAwait(false);

        // EnsureCreated only creates the full schema when the database file is brand new; add tables
        // introduced after the initial release so existing deployments pick them up as well.
        await ExecuteWithBusyRetryAsync(connection, CreateWasteDispatchLogsSql, cancellationToken).ConfigureAwait(false);
        await ExecuteWithBusyRetryAsync(connection, CreateSpontanTreffSql, cancellationToken).ConfigureAwait(false);
        await ExecuteWithBusyRetryAsync(connection, CreateWarningDispatchLogsSql, cancellationToken).ConfigureAwait(false);
        await ExecuteWithBusyRetryAsync(connection, CreateFoodDispatchLogsSql, cancellationToken).ConfigureAwait(false);
        await ExecuteWithBusyRetryAsync(connection, CreatePollenDispatchStatesSql, cancellationToken).ConfigureAwait(false);
        await ExecuteWithBusyRetryAsync(connection, CreateRssDashboardSettingsSql, cancellationToken).ConfigureAwait(false);
        await ExecuteWithBusyRetryAsync(connection, CreateCommandUsageLogsSql, cancellationToken).ConfigureAwait(false);
        await ExecuteWithBusyRetryAsync(connection, CreateWishlistGamesSql, cancellationToken).ConfigureAwait(false);
        await ExecuteWithBusyRetryAsync(connection, CreateWishlistAlertHistoriesSql, cancellationToken).ConfigureAwait(false);
        await ExecuteWithBusyRetryAsync(connection, CreateWishlistIndexesSql, cancellationToken).ConfigureAwait(false);
        await ExecuteWithBusyRetryAsync(connection, CreateCommandUsageIndexesSql, cancellationToken).ConfigureAwait(false);
        await ExecuteWithBusyRetryAsync(connection, CreateUserAccountLinksSql, cancellationToken).ConfigureAwait(false);
        await ExecuteWithBusyRetryAsync(connection, CreateDealWatchItemsSql, cancellationToken).ConfigureAwait(false);
        await ExecuteWithBusyRetryAsync(connection, CreateDiscordScheduledEventsSql, cancellationToken).ConfigureAwait(false);
        await ExecuteWithBusyRetryAsync(connection, CreateRemindersSql, cancellationToken).ConfigureAwait(false);
        await ExecuteWithBusyRetryAsync(connection, CreateDispatchPruneIndexesSql, cancellationToken).ConfigureAwait(false);
        await ExecuteWithBusyRetryAsync(connection, CreateRssFeedsNameIndexSql, cancellationToken).ConfigureAwait(false);

        // Late-added columns: CREATE TABLE IF NOT EXISTS never alters an existing table, and
        // EnsureCreated does nothing on databases that already exist, so older deployments need
        // guarded ADD COLUMN statements. SQLite has no ADD COLUMN IF NOT EXISTS, hence the
        // "duplicate column name" tolerance.
        await AddColumnIfMissingAsync(connection, "SpontanTreffs", "Closed", "INTEGER NOT NULL DEFAULT 0", cancellationToken).ConfigureAwait(false);

        // Index on Closed must run AFTER the column is added.
        await ExecuteWithBusyRetryAsync(connection, CreateSpontanTreffExpiryIndexSql, cancellationToken).ConfigureAwait(false);

        await AddColumnIfMissingAsync(connection, "RssFeeds", "LastItemGuid", "TEXT NULL", cancellationToken).ConfigureAwait(false);
        await AddColumnIfMissingAsync(connection, "RssFeeds", "LastPublishedDate", "TEXT NULL", cancellationToken).ConfigureAwait(false);
        await AddColumnIfMissingAsync(connection, "RssFeeds", "LastPolledAt", "TEXT NULL", cancellationToken).ConfigureAwait(false);
        await AddColumnIfMissingAsync(connection, "RssFeeds", "RefreshIntervalMinutes", "INTEGER NOT NULL DEFAULT 15", cancellationToken).ConfigureAwait(false);
        await AddColumnIfMissingAsync(connection, "RssFeeds", "IsPaused", "INTEGER NOT NULL DEFAULT 0", cancellationToken).ConfigureAwait(false);
        await AddColumnIfMissingAsync(connection, "RssFeeds", "NormalizedName", "TEXT NOT NULL DEFAULT ''", cancellationToken).ConfigureAwait(false);

        // Index on NormalizedName must run AFTER the column is added (see line above).
        await ExecuteWithBusyRetryAsync(connection, CreateRssFeedsNormalizedNameIndexSql, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Number of attempts for schema statements that can transiently fail with SQLITE_BUSY while
    /// another process still holds the database.
    /// </summary>
    private const int SchemaStatementAttempts = 10;

    /// <summary>
    /// Executes a schema statement, retrying while SQLite reports the database as busy or locked.
    /// </summary>
    /// <param name="connection">The open SQLite connection the statement runs on.</param>
    /// <param name="sql">The schema statement (compile-time constant — never user input).</param>
    /// <returns>A task representing the asynchronous schema operation.</returns>
    private static async Task ExecuteWithBusyRetryAsync(SqliteConnection connection, string sql, CancellationToken cancellationToken = default)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await using var command = connection.CreateCommand();
                command.CommandText = sql;
                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                return;
            }
            catch (SqliteException ex) when (IsTransientSqliteFailure(ex) && attempt < SchemaStatementAttempts)
            {
                await Task.Delay(Math.Min(100 * attempt, 1000), cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Adds a column to an existing table, tolerating the "duplicate column name" error SQLite raises
    /// when the column is already present.
    /// </summary>
    /// <param name="connection">The open SQLite connection the statement runs on.</param>
    /// <param name="table">The table to alter (compile-time constant — never user input).</param>
    /// <param name="column">The column to add (compile-time constant — never user input).</param>
    /// <param name="definition">The column definition (type, nullability, default).</param>
    /// <returns>A task representing the asynchronous schema operation.</returns>
    private static async Task AddColumnIfMissingAsync(SqliteConnection connection, string table, string column, string definition, CancellationToken cancellationToken = default)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await using var command = connection.CreateCommand();
                command.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {definition};";
                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                return;
            }
            catch (SqliteException ex) when (IsDuplicateColumn(ex))
            {
                // The column already exists; the schema is at least as new as this code expects.
                return;
            }
            catch (SqliteException ex) when (IsTransientSqliteFailure(ex) && attempt < SchemaStatementAttempts)
            {
                await Task.Delay(Math.Min(100 * attempt, 1000), cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Determines whether a SQLite failure is transient and worth retrying.
    /// </summary>
    /// <param name="ex">The SQLite exception to inspect.</param>
    /// <returns><see langword="true"/> when the failure may succeed on a retry; otherwise, <see langword="false"/>.</returns>
    private static bool IsTransientSqliteFailure(SqliteException ex) =>
        ex.SqliteErrorCode == 5 // SQLITE_BUSY
        || ex.SqliteExtendedErrorCode is 261 or 262 or 517 or 520 // BUSY recovery/snapshot variants
        || ex.Message.Contains("locked", StringComparison.OrdinalIgnoreCase)
        || ex.Message.Contains("busy", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Determines whether a SQLite failure means the column already exists.
    /// </summary>
    /// <param name="ex">The SQLite exception to inspect.</param>
    /// <returns><see langword="true"/> when the failure is a duplicate-column error; otherwise, <see langword="false"/>.</returns>
    private static bool IsDuplicateColumn(SqliteException ex) =>
        ex.Message.Contains("duplicate column name", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Raw DDL for the waste dispatch table so databases created before the feature pick it up as well.
    /// </summary>
    public const string CreateWasteDispatchLogsSql =
        """
        CREATE TABLE IF NOT EXISTS WasteDispatchLogs (
            Kind    TEXT    NOT NULL,
            GuildId INTEGER NOT NULL,
            SentOn  TEXT    NOT NULL,
            PRIMARY KEY (Kind, GuildId, SentOn)
        );
        """;

    /// <summary>
    /// Raw DDL for the food dispatch table so databases created before the feature pick it up as well.
    /// Mirrored by <c>FoodDispatchRepositoryTests</c>, which replays this exact startup sequence.
    /// </summary>
    public const string CreateFoodDispatchLogsSql =
        """
        CREATE TABLE IF NOT EXISTS FoodDispatchLogs (
            Kind   TEXT    NOT NULL,
            SentOn TEXT    NOT NULL,
            PRIMARY KEY (Kind, SentOn)
        );
        """;

    /// <summary>
    /// Raw DDL for the pollen dispatch state table so deployments created before the feature gain it.
    /// </summary>
    public const string CreatePollenDispatchStatesSql =
        """
        CREATE TABLE IF NOT EXISTS PollenDispatchStates (
            GuildId        INTEGER NOT NULL PRIMARY KEY,
            LastPostedDate TEXT    NOT NULL,
            ForecastJson   TEXT    NOT NULL
        );
        """;

    /// <summary>
    /// Raw DDL for the RSS dashboard settings table so databases created before the feature pick it up as well.
    /// </summary>
    public const string CreateRssDashboardSettingsSql =
        """
        CREATE TABLE IF NOT EXISTS RssDashboardSettings (
            Id        INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
            ChannelId INTEGER NOT NULL,
            MessageId INTEGER NOT NULL
        );
        """;

    /// <summary>
    /// Index backing the spontaneous-meetup expiry sweep, which runs every minute and filters on
    /// <c>Closed</c>/<c>ExpiresAt</c>. Applied both here (existing databases) and via
    /// <c>BotDbContext.OnModelCreating</c> (databases created by <c>EnsureCreated</c>).
    /// </summary>
    public const string CreateSpontanTreffExpiryIndexSql =
        """
        CREATE INDEX IF NOT EXISTS IX_SpontanTreffs_Closed_ExpiresAt ON SpontanTreffs (Closed, ExpiresAt);
        """;

    /// <summary>
    /// Raw DDL for the DWD warning dispatch table so databases created before the feature pick it up as well.
    /// Mirrored by <c>WarningDispatchRepositoryTests</c>, which replays this exact startup sequence.
    /// </summary>
    public const string CreateWarningDispatchLogsSql =
        """
        CREATE TABLE IF NOT EXISTS WarningDispatchLogs (
            WarningId TEXT    NOT NULL,
            GuildId   INTEGER NOT NULL,
            SentAt    TEXT    NOT NULL,
            PRIMARY KEY (WarningId, GuildId)
        );
        """;

    /// <summary>
    /// Raw DDL for the command usage metrics table so databases created before the feature pick it up as well.
    /// </summary>
    public const string CreateCommandUsageLogsSql =
        """
        CREATE TABLE IF NOT EXISTS CommandUsageLogs (
            Id          INTEGER PRIMARY KEY AUTOINCREMENT,
            CommandName TEXT    NOT NULL,
            UserId      INTEGER NOT NULL,
            GuildId     INTEGER NOT NULL,
            InvokedAt   TEXT    NOT NULL,
            Succeeded   INTEGER NOT NULL DEFAULT 1
        );
        """;

    /// <summary>
    /// Raw DDL for the spontaneous meetup tables so databases created before the feature pick them up as well.
    /// Mirrored by <c>SpontanTreffRepositoryTests</c>, which replays this exact startup sequence.
    /// </summary>
    public const string CreateSpontanTreffSql =
        """
        CREATE TABLE IF NOT EXISTS SpontanTreffs (
            Id            INTEGER PRIMARY KEY AUTOINCREMENT,
            GuildId       INTEGER NOT NULL,
            ChannelId     INTEGER NOT NULL,
            MessageId     INTEGER NOT NULL,
            OrganizerId   INTEGER NOT NULL,
            OrganizerName TEXT    NOT NULL,
            Plan          TEXT    NOT NULL,
            Location      TEXT    NULL,
            CreatedAt     TEXT    NOT NULL,
            ExpiresAt     TEXT    NOT NULL,
            Closed        INTEGER NOT NULL DEFAULT 0
        );
        CREATE TABLE IF NOT EXISTS SpontanTreffResponses (
            MeetupId    INTEGER NOT NULL REFERENCES SpontanTreffs(Id) ON DELETE CASCADE,
            UserId      INTEGER NOT NULL,
            Going       INTEGER NOT NULL,
            RespondedAt TEXT    NOT NULL,
            PRIMARY KEY (MeetupId, UserId)
        );
        """;


    /// <summary>
    /// Raw DDL for lingering wishlist tracking tables so deployments made before the feature shipped gain the collection.
    /// </summary>
    public const string CreateWishlistGamesSql =
        """
        CREATE TABLE IF NOT EXISTS WishlistGames (
            Id          INTEGER PRIMARY KEY AUTOINCREMENT,
            AppId       INTEGER NOT NULL UNIQUE,
            Name        TEXT    NOT NULL,
            LastUpdated TEXT    NOT NULL
        );
        """;

    /// <summary>
    /// Raw DDL for lingering wishlist alert history rows.
    /// </summary>
    public const string CreateWishlistAlertHistoriesSql =
        """
        CREATE TABLE IF NOT EXISTS WishlistAlertHistories (
            Id               INTEGER PRIMARY KEY AUTOINCREMENT,
            AppId            INTEGER NOT NULL,
            AlertedAt        TEXT    NOT NULL,
            WasHistoricalLow INTEGER NOT NULL
        );
        """;

    /// <summary>
    /// Companion indexes for the wishlist history table on older deployments.
    /// </summary>
    public const string CreateWishlistIndexesSql =
        """
        CREATE UNIQUE INDEX IF NOT EXISTS IX_WishlistGames_AppId ON WishlistGames (AppId);
        CREATE INDEX IF NOT EXISTS IX_WishlistAlertHistories_AppId_AlertedAt ON WishlistAlertHistories (AppId, AlertedAt);
        CREATE INDEX IF NOT EXISTS IX_WishlistAlertHistories_AlertedAt ON WishlistAlertHistories (AlertedAt);
        """;

    /// <summary>
    /// Raw DDL for dispatch-log prune indexes so existing deployments gain the indexes added in EF Core
    /// model configuration. Mirrored by <c>BotDbContext.OnModelCreating</c>.
    /// </summary>
    public const string CreateDispatchPruneIndexesSql =
        """
        CREATE INDEX IF NOT EXISTS IX_WasteDispatchLogs_SentOn ON WasteDispatchLogs (SentOn);
        CREATE INDEX IF NOT EXISTS IX_WarningDispatchLogs_SentAt ON WarningDispatchLogs (SentAt);
        CREATE INDEX IF NOT EXISTS IX_FoodDispatchLogs_SentOn ON FoodDispatchLogs (SentOn);
        """;

    /// <summary>
    /// Raw DDL for the RSS feeds name index.
    /// </summary>
    public const string CreateRssFeedsNameIndexSql =
        """
        CREATE INDEX IF NOT EXISTS IX_RssFeeds_Name ON RssFeeds (Name);
        """;

    /// <summary>
    /// Raw DDL for the RSS feeds normalized name index.
    /// </summary>
    public const string CreateRssFeedsNormalizedNameIndexSql =
        """
        CREATE INDEX IF NOT EXISTS IX_RssFeeds_NormalizedName ON RssFeeds (NormalizedName);
        """;

    /// <summary>
    /// Raw DDL for Discord user to external account links so deployments created before the feature gain the table.
    /// </summary>
    public const string CreateUserAccountLinksSql =
        """
        CREATE TABLE IF NOT EXISTS UserAccountLinks (
            Id            INTEGER PRIMARY KEY AUTOINCREMENT,
            DiscordUserId INTEGER NOT NULL,
            Provider      TEXT    NOT NULL,
            ExternalId    TEXT    NOT NULL,
            DisplayName   TEXT    NULL,
            RefreshToken  TEXT    NULL,
            Region        TEXT    NULL,
            WishlistUrl   TEXT    NULL,
            LinkedAt      TEXT    NOT NULL
        );
        CREATE UNIQUE INDEX IF NOT EXISTS IX_UserAccountLinks_DiscordUserId_Provider ON UserAccountLinks (DiscordUserId, Provider);
        """;

    /// <summary>
    /// Raw DDL for the per-user deal watchlist table so deployments created before the feature gain the table.
    /// </summary>
    public const string CreateDealWatchItemsSql =
        """
        CREATE TABLE IF NOT EXISTS DealWatchItems (
            Id                 INTEGER PRIMARY KEY AUTOINCREMENT,
            DiscordUserId      INTEGER NOT NULL,
            GameName           TEXT    NOT NULL,
            ItadId             TEXT    NULL,
            MinDiscountPercent INTEGER NOT NULL DEFAULT 40,
            TargetPrice        TEXT    NULL,
            LastKnownPrice     TEXT    NULL,
            LastKnownDiscount  INTEGER NOT NULL DEFAULT 0,
            CreatedAt          TEXT    NOT NULL,
            LastAlertedAt      TEXT    NULL
        );
        CREATE UNIQUE INDEX IF NOT EXISTS IX_DealWatchItems_DiscordUserId_GameName ON DealWatchItems (DiscordUserId, GameName);
        """;

    /// <summary>
    /// Raw DDL for the Discord guild scheduled event tracking table so expired events can be deleted.
    /// </summary>
    public const string CreateDiscordScheduledEventsSql =
        """
        CREATE TABLE IF NOT EXISTS DiscordScheduledEvents (
            Id        INTEGER PRIMARY KEY AUTOINCREMENT,
            GuildId   INTEGER NOT NULL,
            EventId   INTEGER NOT NULL,
            Kind      TEXT    NOT NULL,
            Name      TEXT    NOT NULL,
            EndAt     TEXT    NOT NULL,
            CreatedAt TEXT    NOT NULL
        );
        CREATE INDEX IF NOT EXISTS IX_DiscordScheduledEvents_EndAt ON DiscordScheduledEvents (EndAt);
        """;

    /// <summary>
    /// Raw DDL for the personal reminder table so deployments created before the feature gain it.
    /// </summary>
    public const string CreateRemindersSql =
        """
        CREATE TABLE IF NOT EXISTS Reminders (
            Id            INTEGER PRIMARY KEY AUTOINCREMENT,
            DiscordUserId INTEGER NOT NULL,
            GuildId       INTEGER NOT NULL DEFAULT 0,
            ChannelId     INTEGER NOT NULL DEFAULT 0,
            Text          TEXT    NOT NULL,
            DueAt         TEXT    NOT NULL,
            CreatedAt     TEXT    NOT NULL,
            DeliveredAt   TEXT    NULL
        );
        CREATE INDEX IF NOT EXISTS IX_Reminders_DeliveredAt_DueAt ON Reminders (DeliveredAt, DueAt);
        CREATE INDEX IF NOT EXISTS IX_Reminders_DiscordUserId ON Reminders (DiscordUserId);
        CREATE INDEX IF NOT EXISTS IX_Reminders_DiscordUserId_DeliveredAt ON Reminders (DiscordUserId, DeliveredAt);
        """;

    /// <summary>
    /// Companion indexes for delayed command usage log deployments.
    /// </summary>
    public const string CreateCommandUsageIndexesSql =
        """
        CREATE INDEX IF NOT EXISTS IX_CommandUsageLogs_InvokedAt ON CommandUsageLogs (InvokedAt);
        CREATE INDEX IF NOT EXISTS IX_CommandUsageLogs_CommandName ON CommandUsageLogs (CommandName);
        CREATE INDEX IF NOT EXISTS IX_CommandUsageLogs_CommandName_InvokedAt ON CommandUsageLogs (CommandName, InvokedAt);
        """;
}
