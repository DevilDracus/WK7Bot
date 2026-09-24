namespace WK7Bot.Services;

using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Threading;
using System.Threading.Tasks;
using WK7Bot.Options;

/// <summary>
/// Manages the background connection lifecycle for the Discord bot client.
/// </summary>
public class DiscordBotWorker : BackgroundService
{
    private readonly DiscordSocketClient _client;
    private readonly Wk7BotOptions _options;
    private readonly ILogger<DiscordBotWorker> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="DiscordBotWorker"/> class with injected dependencies.
    /// </summary>
    /// <param name="client">The active Discord gateway socket client.</param>
    /// <param name="options">The strongly-typed application configuration options.</param>
    /// <param name="logger">The diagnostic logging service.</param>
    public DiscordBotWorker(
        DiscordSocketClient client,
        IOptions<Wk7BotOptions> options,
        ILogger<DiscordBotWorker> logger)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
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
            _logger.LogCritical("No valid Discord bot token was provided in configuration. Check your options or environment variables.");
            return;
        }

        _logger.LogInformation("Attempting to connect to Discord gateway...");

        await _client.LoginAsync(TokenType.Bot, token);
        await _client.StartAsync();

        await Task.Delay(-1, stoppingToken);
    }

    /// <summary>
    /// Resolves the Discord bot token, falling back to environment variables if unpopulated in strongly-typed options.
    /// </summary>
    /// <returns>The resolved Discord bot token string, or null if no valid token was found.</returns>
    private string? GetDiscordToken()
    {
        if (!string.IsNullOrWhiteSpace(_options.DiscordToken))
        {
            return _options.DiscordToken;
        }

        return Environment.GetEnvironmentVariable("DISCORD_BOT_TOKEN");
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