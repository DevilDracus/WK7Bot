namespace WK7Bot.Services;

using Discord.WebSocket;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.Json;

/// <summary>
/// Hosted background service monitoring Discord gateway messages for target user mentions
/// and forwarding queued notifications to the getnotify.me Alexa API using token and secret credentials.
/// </summary>
public class AlexaMentionNotificationService : IHostedService
{
    private readonly DiscordSocketClient _discordClient;
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AlexaMentionNotificationService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="AlexaMentionNotificationService"/> class.
    /// </summary>
    /// <param name="discordClient">The active Discord socket client instance.</param>
    /// <param name="httpClient">The HTTP client instance for outbound REST API requests.</param>
    /// <param name="configuration">The application configuration providing target user and API credentials.</param>
    /// <param name="logger">The logger instance for operational diagnostics.</param>
    public AlexaMentionNotificationService(
        DiscordSocketClient discordClient,
        HttpClient httpClient,
        IConfiguration configuration,
        ILogger<AlexaMentionNotificationService> logger)
    {
        _discordClient = discordClient ?? throw new ArgumentNullException(nameof(discordClient));
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Binds the message received event handler to the Discord socket client when the background service starts.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token monitored during application startup.</param>
    /// <returns>A completed task representing startup initialization.</returns>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _discordClient.MessageReceived += HandleMessageReceivedAsync;
        return Task.CompletedTask;
    }

    /// <summary>
    /// Unbinds the message received event handler from the Discord socket client when the service stops.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token monitored during application shutdown.</param>
    /// <returns>A completed task representing shutdown cleanup.</returns>
    public Task StopAsync(CancellationToken cancellationToken)
    {
        _discordClient.MessageReceived -= HandleMessageReceivedAsync;
        return Task.CompletedTask;
    }

    /// <summary>
    /// Evaluates incoming Discord socket messages to detect mentions of the configured target user ID.
    /// </summary>
    /// <param name="rawMessage">The raw incoming socket message received from the Discord gateway.</param>
    /// <returns>A task representing the asynchronous message evaluation process.</returns>
    private async Task HandleMessageReceivedAsync(SocketMessage rawMessage)
    {
        try
        {
            if (rawMessage is not SocketUserMessage userMessage || userMessage.Author.IsBot)
            {
                return;
            }

            var targetUserIdStr = _configuration["alexa_notification:target_user_id"];
            if (!ulong.TryParse(targetUserIdStr, out var targetUserId))
            {
                return;
            }

            if (userMessage.MentionedUsers.Any(u => u.Id == targetUserId))
            {
                var authorName = userMessage.Author.GlobalName ?? userMessage.Author.Username;
                var notificationText = $"Neue Nachricht von {authorName}, die Nachricht enthält: {userMessage.CleanContent}";

                _logger.LogInformation("Discord mention detected for user {TargetUserId}. Forwarding to Alexa API.", targetUserId);

                await SendAlexaNotificationAsync(notificationText);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred while evaluating incoming Discord message for Alexa notifications.");
        }
    }

    /// <summary>
    /// Dispatches the formatted notification payload along with API token and secret credentials to getnotify.me.
    /// </summary>
    /// <param name="notificationText">The formatted message content to be queued for Alexa.</param>
    /// <returns>A task representing the asynchronous HTTP request operation.</returns>
    private async Task SendAlexaNotificationAsync(string notificationText)
    {
        var apiToken = _configuration["alexa_notification:api_token"];
        var apiSecret = _configuration["alexa_notification:api_secret"];
        var endpointUrl = _configuration["alexa_notification:endpoint_url"] ?? "https://api.getnotify.me/v1/notify";

        if (string.IsNullOrWhiteSpace(apiToken) || string.IsNullOrWhiteSpace(apiSecret))
        {
            _logger.LogWarning("Alexa notification API token or secret is not configured in settings. Skipping dispatch.");
            return;
        }

        var payload = new
        {
            token = apiToken,
            secret = apiSecret,
            notification = notificationText
        };

        var json = JsonSerializer.Serialize(payload);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        try
        {
            using var response = await _httpClient.PostAsync(endpointUrl, content);
            response.EnsureSuccessStatusCode();

            _logger.LogInformation("Successfully dispatched persistent notification to getnotify.me API.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to deliver persistent notification to getnotify.me API.");
        }
    }
}