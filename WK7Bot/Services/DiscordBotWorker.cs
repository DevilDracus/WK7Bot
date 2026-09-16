using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace WK7Bot.Services;

/// <summary>
/// Manages the background connection lifecycle for the Discord bot client.
/// </summary>
public class DiscordBotWorker : BackgroundService
{
    private readonly DiscordSocketClient _client;
    private readonly IConfiguration _configuration;
    private readonly ILogger<DiscordBotWorker> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="DiscordBotWorker"/> class with injected dependencies.
    /// </summary>
    /// <param name="client">The active Discord gateway socket client.</param>
    /// <param name="configuration">Application configuration loaded from environment variables, user secrets, and JSON options.</param>
    /// <param name="logger">The diagnostic logging service.</param>
    public DiscordBotWorker(
        DiscordSocketClient client,
        IConfiguration configuration,
        ILogger<DiscordBotWorker> logger)
    {
        _client = client;
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>
    /// Connects to the Discord gateway and maintains active background execution.
    /// </summary>
    /// <param name="stoppingToken">Cancellation token triggered when the application host stops.</param>
    /// <returns>A long-running task representing the service execution lifecycle.</returns>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _client.Log += OnLogAsync;

        string? token = GetDiscordToken();

        if (string.IsNullOrWhiteSpace(token))
        {
            _logger.LogCritical("No valid Discord bot token was provided in configuration. Check your secrets or appsettings.json.");
            return;
        }

        _logger.LogInformation("Attempting to connect to Discord gateway...");

        await _client.LoginAsync(TokenType.Bot, token);
        await _client.StartAsync();

        await Task.Delay(-1, stoppingToken);
    }

    /// <summary>
    /// Resolves the Discord bot token across multiple configuration sources including .NET User Secrets, Home Assistant options, and environment variables.
    /// </summary>
    /// <returns>The resolved Discord bot token string, or null if no valid token was found.</returns>
    private string? GetDiscordToken()
    {
        return _configuration["Discord:Token"]
            ?? _configuration["discord_token"]
            ?? _configuration["BotToken"]
            ?? _configuration["token"]
            ?? Environment.GetEnvironmentVariable("DISCORD_BOT_TOKEN");
    }

    /// <summary>
    /// Forwards diagnostic events emitted by the Discord socket client into the application logger.
    /// </summary>
    /// <param name="message">The incoming event payload containing severity, source, and message text.</param>
    /// <returns>A completed task representing the log forwarding operation.</returns>
    private Task OnLogAsync(LogMessage message)
    {
        var logLevel = message.Severity switch
        {
            LogSeverity.Critical => LogLevel.Critical,
            LogSeverity.Error => LogLevel.Error,
            LogSeverity.Warning => LogLevel.Warning,
            LogSeverity.Info => LogLevel.Information,
            LogSeverity.Verbose => LogLevel.Debug,
            LogSeverity.Debug => LogLevel.Trace,
            _ => LogLevel.Information
        };

        _logger.Log(logLevel, message.Exception, "Discord.Net [{Source}]: {Message}", message.Source, message.Message);
        return Task.CompletedTask;
    }
}