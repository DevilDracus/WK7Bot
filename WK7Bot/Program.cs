using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using WK7Bot.Extensions;
using WK7Bot.Infrastructure.Data;
using WK7Bot.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddJsonFile("/data/options.json", optional: true, reloadOnChange: true);

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") ?? "Data Source=/data/wk7bot.db";

// Modular service registrations
builder.Services.AddBotDatabase(connectionString);
builder.Services.AddBotDiscordAndClients();
builder.Services.AddBotHostedServices(builder.Configuration);

var wk7Section = builder.Configuration.GetSection(Wk7BotOptions.SectionName);

if (wk7Section.Exists())
{
    // Binds from "Wk7Bot": { ... } section in appsettings
    builder.Services.Configure<Wk7BotOptions>(wk7Section);
}
else
{
    // Binds directly from root (Home Assistant config.yaml / options.json)
    builder.Services.Configure<Wk7BotOptions>(builder.Configuration);
}

var app = builder.Build();

await app.InitializeDatabaseAsync();

app.MapGet("/health", () => Results.Ok("WK7 Bot is healthy."));

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
    }

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