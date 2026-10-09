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

        // WAL lets the background services read while a writer commits instead of failing with
        // "database is locked"; busy_timeout makes writers wait for a competing lock instead of
        // erroring immediately. Both statements are harmless for in-memory databases.
        await dbContext.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;");
        await dbContext.Database.ExecuteSqlRawAsync("PRAGMA busy_timeout=5000;");

        await dbContext.Database.EnsureCreatedAsync();

        // EnsureCreated only creates the full schema when the database file is brand new; add tables
        // introduced after the initial release so existing deployments pick them up as well.
        await dbContext.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS WasteDispatchLogs (
                Kind    TEXT    NOT NULL,
                GuildId INTEGER NOT NULL,
                SentOn  TEXT    NOT NULL,
                PRIMARY KEY (Kind, GuildId, SentOn)
            );
            """);

        await dbContext.Database.ExecuteSqlRawAsync(CreateSpontanTreffSql);
        await dbContext.Database.ExecuteSqlRawAsync(CreateWarningDispatchLogsSql);
        await dbContext.Database.ExecuteSqlRawAsync(CreateFoodDispatchLogsSql);
        await dbContext.Database.ExecuteSqlRawAsync(CreateRssDashboardSettingsSql);
        await dbContext.Database.ExecuteSqlRawAsync(CreateSpontanTreffExpiryIndexSql);

        // Late-added columns: CREATE TABLE IF NOT EXISTS never alters an existing table, and
        // EnsureCreated does nothing on databases that already exist, so older deployments need
        // guarded ADD COLUMN statements. SQLite has no ADD COLUMN IF NOT EXISTS, hence the
        // "duplicate column name" tolerance.
        await AddColumnIfMissingAsync(dbContext, "SpontanTreffs", "Closed", "INTEGER NOT NULL DEFAULT 0");
        await AddColumnIfMissingAsync(dbContext, "RssFeeds", "LastItemGuid", "TEXT NULL");
        await AddColumnIfMissingAsync(dbContext, "RssFeeds", "LastPublishedDate", "TEXT NULL");
        await AddColumnIfMissingAsync(dbContext, "RssFeeds", "LastPolledAt", "TEXT NULL");
        await AddColumnIfMissingAsync(dbContext, "RssFeeds", "RefreshIntervalMinutes", "INTEGER NOT NULL DEFAULT 15");
    }

    /// <summary>
    /// Adds a column to an existing table, tolerating the "duplicate column name" error SQLite raises
    /// when the column is already present.
    /// </summary>
    /// <param name="dbContext">The database context used to run the statement.</param>
    /// <param name="table">The table to alter (compile-time constant — never user input).</param>
    /// <param name="column">The column to add (compile-time constant — never user input).</param>
    /// <param name="definition">The column definition (type, nullability, default).</param>
    /// <returns>A task representing the asynchronous schema operation.</returns>
    private static async Task AddColumnIfMissingAsync(BotDbContext dbContext, string table, string column, string definition)
    {
        try
        {
            await dbContext.Database.ExecuteSqlRawAsync($"ALTER TABLE {table} ADD COLUMN {column} {definition};");
        }
        catch (SqliteException ex) when (ex.Message.Contains("duplicate column name", StringComparison.OrdinalIgnoreCase))
        {
            // The column already exists; the schema is at least as new as this code expects.
        }
    }

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