using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.AspNetCore.Builder;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MQTTnet;
using WK7Bot.Core.Interfaces;
using WK7Bot.Infrastructure.Data;
using WK7Bot.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddJsonFile("/data/options.json", optional: true, reloadOnChange: true);

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") ?? "Data Source=/data/wk7bot.db";

builder.Services.AddDbContext<BotDbContext>(options =>
    options.UseSqlite(connectionString));

builder.Services.AddScoped<IRssRepository, RssRepository>();
builder.Services.AddTransient<RssParserService>();

builder.Services.AddSingleton(new DiscordSocketClient(new DiscordSocketConfig
{
    GatewayIntents = GatewayIntents.AllUnprivileged | GatewayIntents.MessageContent | GatewayIntents.GuildMembers | GatewayIntents.GuildPresences,
    AlwaysDownloadUsers = true
}));

builder.Services.AddSingleton(x => new InteractionService(x.GetRequiredService<DiscordSocketClient>()));
builder.Services.AddHttpClient<IHomeAssistantService, HomeAssistantService>();
builder.Services.AddHttpClient<ILeipzigWasteService, LeipzigWasteService>();

builder.Services.AddSingleton<IMqttClient>(sp => new MqttClientFactory().CreateMqttClient());

builder.Services.AddHostedService<InteractionHandlingService>();
builder.Services.AddHostedService<RssPollingBackgroundService>();
builder.Services.AddHostedService<DiscordBotWorker>();
builder.Services.AddHostedService<HomeAssistantNotifierService>();
builder.Services.AddHostedService<LeipzigWasteBackgroundService>();

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
    }
}