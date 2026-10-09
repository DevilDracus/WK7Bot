using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using WK7Bot.Extensions;
using WK7Bot.Infrastructure.Data;
using WK7Bot.Models;
using WK7Bot.Options;
using WK7Bot.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddJsonFile("/data/options.json", optional: true, reloadOnChange: false);

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") ?? "Data Source=/data/wk7bot.db";

// Capture every Error/Critical log event for Discord direct message delivery in addition to the regular logs.
var errorNotificationQueue = new ErrorNotificationQueue();
builder.Services.AddSingleton(errorNotificationQueue);
builder.Logging.AddProvider(new DiscordErrorLoggerProvider(errorNotificationQueue));

// Modular service registrations
builder.Services.AddBotDatabase(connectionString);
builder.Services.AddBotDiscordAndClients();
builder.Services.AddBotHostedServices(builder.Configuration);

var wk7Section = builder.Configuration.GetSection(Wk7BotOptions.SectionName);

// Bind the appsettings "Wk7Bot" section first so its defaults fill the gaps, then re-bind from the
// configuration root (Home Assistant /data/options.json) so user-provided flat values override the
// shipped defaults for every key they actually define. Binding only the section (the old
// section-else-root logic) let appsettings shadow the HA add-on's runtime options entirely.
if (wk7Section.Exists())
{
    builder.Services.Configure<Wk7BotOptions>(wk7Section);
}

builder.Services.Configure<Wk7BotOptions>(builder.Configuration);

var app = builder.Build();

await app.InitializeDatabaseAsync();

app.MapGet("/health", () => Results.Ok("WK7 Bot is healthy."));

// Route process-level exception escapes into the Discord error notification queue as well.
AppDomain.CurrentDomain.UnhandledException += (_, eventArgs) =>
{
    errorNotificationQueue.Enqueue(eventArgs.ExceptionObject is Exception exception
        ? ErrorNotification.FromException("AppDomain.UnhandledException", exception)
        : new ErrorNotification
        {
            Context = "AppDomain.UnhandledException",
            Message = eventArgs.ExceptionObject?.ToString() ?? "Unbekannter Fehler",
            Timestamp = DateTimeOffset.Now
        });
};

TaskScheduler.UnobservedTaskException += (_, eventArgs) =>
{
    eventArgs.SetObserved();

    foreach (var exception in eventArgs.Exception.InnerExceptions)
    {
        errorNotificationQueue.Enqueue(ErrorNotification.FromException("TaskScheduler.UnobservedTaskException", exception));
    }
};

await app.RunAsync();

/// <summary>
/// Extension methods for application database lifecycle and schema initialization.
/// </summary>
public static class DatabaseInitializationExtensions
{
    /// <summary>
    /// Ensures that the parent directory exists and that the SQLite database file and schema are fully initialized on application startup.
    /// </summary>
    /// <param name="app">The running web application instance providing service resolution access.</param>
    /// <returns>A task representing the asynchronous database startup operation.</returns>
    public static async Task InitializeDatabaseAsync(this WebApplication app)
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

        await dbContext.Database.EnsureCreatedAsync();

        // Everything below runs on a raw ADO connection on purpose: EF Core logs every failed
        // DbCommand at Error level — which feeds the admin error-DM queue — before any catch can
        // swallow it, and SQLite has no ADD COLUMN IF NOT EXISTS, so "duplicate column name" is an
        // expected outcome of these statements. The raw connection also lets the statements ride
        // out "database is locked" while a previous process is still shutting down.
        await using var connection = new SqliteConnection(dbContext.Database.GetConnectionString());
        await connection.OpenAsync();

        // WAL lets the background services read while a writer commits instead of failing with
        // "database is locked"; busy_timeout makes writers wait for a competing lock instead of
        // erroring immediately. Both statements are harmless for in-memory databases.
        await ExecuteWithBusyRetryAsync(connection, "PRAGMA busy_timeout=5000;");
        await ExecuteWithBusyRetryAsync(connection, "PRAGMA journal_mode=WAL;");

        // EnsureCreated only creates the full schema when the database file is brand new; add tables
        // introduced after the initial release so existing deployments pick them up as well.
        await ExecuteWithBusyRetryAsync(connection, CreateWasteDispatchLogsSql);
        await ExecuteWithBusyRetryAsync(connection, CreateSpontanTreffSql);
        await ExecuteWithBusyRetryAsync(connection, CreateWarningDispatchLogsSql);
        await ExecuteWithBusyRetryAsync(connection, CreateFoodDispatchLogsSql);
        await ExecuteWithBusyRetryAsync(connection, CreateRssDashboardSettingsSql);
        await ExecuteWithBusyRetryAsync(connection, CreateSpontanTreffExpiryIndexSql);

        // Late-added columns: CREATE TABLE IF NOT EXISTS never alters an existing table, and
        // EnsureCreated does nothing on databases that already exist, so older deployments need
        // guarded ADD COLUMN statements. SQLite has no ADD COLUMN IF NOT EXISTS, hence the
        // "duplicate column name" tolerance.
        await AddColumnIfMissingAsync(connection, "SpontanTreffs", "Closed", "INTEGER NOT NULL DEFAULT 0");
        await AddColumnIfMissingAsync(connection, "RssFeeds", "LastItemGuid", "TEXT NULL");
        await AddColumnIfMissingAsync(connection, "RssFeeds", "LastPublishedDate", "TEXT NULL");
        await AddColumnIfMissingAsync(connection, "RssFeeds", "LastPolledAt", "TEXT NULL");
        await AddColumnIfMissingAsync(connection, "RssFeeds", "RefreshIntervalMinutes", "INTEGER NOT NULL DEFAULT 15");
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
    private static async Task ExecuteWithBusyRetryAsync(SqliteConnection connection, string sql)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await using var command = connection.CreateCommand();
                command.CommandText = sql;
                await command.ExecuteNonQueryAsync();
                return;
            }
            catch (SqliteException ex) when (IsTransientSqliteFailure(ex) && attempt < SchemaStatementAttempts)
            {
                await Task.Delay(Math.Min(100 * attempt, 1000));
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
    private static async Task AddColumnIfMissingAsync(SqliteConnection connection, string table, string column, string definition)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await using var command = connection.CreateCommand();
                command.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {definition};";
                await command.ExecuteNonQueryAsync();
                return;
            }
            catch (SqliteException ex) when (IsDuplicateColumn(ex))
            {
                // The column already exists; the schema is at least as new as this code expects.
                return;
            }
            catch (SqliteException ex) when (IsTransientSqliteFailure(ex) && attempt < SchemaStatementAttempts)
            {
                await Task.Delay(Math.Min(100 * attempt, 1000));
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
            MeetupId    INTEGER NOT NULL,
            UserId      INTEGER NOT NULL,
            Going       INTEGER NOT NULL,
            RespondedAt TEXT    NOT NULL,
            PRIMARY KEY (MeetupId, UserId)
        );
        """;
}