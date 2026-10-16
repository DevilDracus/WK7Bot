using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using WK7Bot.Core.Utilities;
using WK7Bot.Extensions;
using WK7Bot.Infrastructure.Data;
using WK7Bot.Models;
using WK7Bot.Options;
using WK7Bot.Services;
using WK7Bot.Testing;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddJsonFile("/data/options.json", optional: true, reloadOnChange: false);

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") ?? "Data Source=/data/wk7bot.db";

// Replace the default ASP.NET Core console logger with our own so the minimum level
// from appsettings.json ("Information") does not leak through a second provider.
builder.Logging.ClearProviders();

// Capture every Error/Critical log event for Discord direct message delivery in addition to the regular logs.
var errorNotificationQueue = new ErrorNotificationQueue();
builder.Services.AddSingleton(errorNotificationQueue);
builder.Logging.AddProvider(new DiscordErrorLoggerProvider(errorNotificationQueue));

// Configure minimum log level from add-on options (defaults to Warning to keep HA log output concise).
var configuredLevel = builder.Configuration["log_level"] ?? "Warning";
if (Enum.TryParse<LogLevel>(configuredLevel, true, out var parsedLevel))
{
    builder.Logging.SetMinimumLevel(parsedLevel);
}
else
{
    builder.Logging.SetMinimumLevel(LogLevel.Warning);
}

// Replace all framework logging filter rules (including appsettings.json) with the single
// operator-selected minimum so no category can emit below Warning unless explicitly configured.
builder.Services.PostConfigure<LoggerFilterOptions>(options =>
{
    options.Rules.Clear();
    options.AddFilter((_, level) => level >= parsedLevel);
});

// Avoid using "SetMinimumLevel" alone, which can leave category-specific rules in place.
builder.Logging.AddFilter((_, level) => level >= parsedLevel);

// Add timestamps to every console log line so the Home Assistant add-on log is readable.
builder.Logging.AddSimpleConsole(options =>
{
    options.SingleLine = true;
    options.TimestampFormat = "[yyyy-MM-dd HH:mm:ss] ";
});

// Modular service registrations
builder.Services.AddBotDatabase(connectionString);
builder.Services.AddBotDiscordAndClients();

// Configure Wk7BotOptions first so FeatureGate (resolved in AddBotHostedServices via DI) sees bound values.
var wk7Section = builder.Configuration.GetSection(Wk7BotOptions.SectionName);
if (wk7Section.Exists())
{
    builder.Services.Configure<Wk7BotOptions>(wk7Section);
}
builder.Services.Configure<Wk7BotOptions>(builder.Configuration);

builder.Services.AddBotHostedServices(builder.Configuration);

var app = builder.Build();

var logger = app.Services.GetRequiredService<ILogger<Program>>();
logger.LogInformation("==== WK7 Bot v{Version} starting at {Timestamp:yyyy-MM-dd HH:mm:ss} ====", AppInformation.Version, DateTime.UtcNow);

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

app.MapTestCommandEndpoints();

await app.RunAsync();


/// <summary>
/// Backward-compatible alias for code (and tests) that still reference the original type name.
/// New code should depend on <see cref="DatabaseSchemaInitializer"/>.
/// </summary>
public static class DatabaseInitializationExtensions
{
    /// <inheritdoc cref="DatabaseSchemaInitializer.CreateWasteDispatchLogsSql"/>
    public const string CreateWasteDispatchLogsSql = DatabaseSchemaInitializer.CreateWasteDispatchLogsSql;
    /// <inheritdoc cref="DatabaseSchemaInitializer.CreateFoodDispatchLogsSql"/>
    public const string CreateFoodDispatchLogsSql = DatabaseSchemaInitializer.CreateFoodDispatchLogsSql;
    /// <inheritdoc cref="DatabaseSchemaInitializer.CreateRssDashboardSettingsSql"/>
    public const string CreateRssDashboardSettingsSql = DatabaseSchemaInitializer.CreateRssDashboardSettingsSql;
    /// <inheritdoc cref="DatabaseSchemaInitializer.CreateSpontanTreffExpiryIndexSql"/>
    public const string CreateSpontanTreffExpiryIndexSql = DatabaseSchemaInitializer.CreateSpontanTreffExpiryIndexSql;
    /// <inheritdoc cref="DatabaseSchemaInitializer.CreateWarningDispatchLogsSql"/>
    public const string CreateWarningDispatchLogsSql = DatabaseSchemaInitializer.CreateWarningDispatchLogsSql;
    /// <inheritdoc cref="DatabaseSchemaInitializer.CreateCommandUsageLogsSql"/>
    public const string CreateCommandUsageLogsSql = DatabaseSchemaInitializer.CreateCommandUsageLogsSql;
    /// <inheritdoc cref="DatabaseSchemaInitializer.CreateSpontanTreffSql"/>
    public const string CreateSpontanTreffSql = DatabaseSchemaInitializer.CreateSpontanTreffSql;
}
