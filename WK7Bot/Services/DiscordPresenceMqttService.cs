namespace WK7Bot.Services;

using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MQTTnet;
using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WK7Bot.Models;

/// <summary>
/// Listens to Discord presence updates, extracts user status and activity artwork, and publishes unified user entities to Home Assistant via MQTT Discovery.
/// </summary>
public class DiscordPresenceMqttService : BackgroundService
{
    private readonly DiscordSocketClient _discordClient;
    private readonly IMqttClient _mqttClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<DiscordPresenceMqttService> _logger;
    private readonly ConcurrentDictionary<ulong, bool> _discoveredUsers = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="DiscordPresenceMqttService"/> class.
    /// </summary>
    /// <param name="discordClient">The active Discord socket client handling server connections.</param>
    /// <param name="mqttClient">The connected MQTT client instance responsible for broker communication.</param>
    /// <param name="configuration">The application configuration provider containing MQTT connection parameters.</param>
    /// <param name="logger">The logging service instance for operational diagnostics.</param>
    public DiscordPresenceMqttService(
        DiscordSocketClient discordClient,
        IMqttClient mqttClient,
        IConfiguration configuration,
        ILogger<DiscordPresenceMqttService> logger)
    {
        _discordClient = discordClient ?? throw new ArgumentNullException(nameof(discordClient));
        _mqttClient = mqttClient ?? throw new ArgumentNullException(nameof(mqttClient));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Registers presence update handlers, connects the MQTT client, and maintains background execution.
    /// </summary>
    /// <param name="stoppingToken">Cancellation token monitored for background service termination.</param>
    /// <returns>A task representing the asynchronous lifecycle of the service.</returns>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await ConnectMqttClientAsync(stoppingToken);

        _discordClient.PresenceUpdated += OnPresenceUpdatedAsync;
        _discordClient.Ready += OnDiscordReadyAsync;

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // Host is shutting down cleanly
        }
        finally
        {
            _discordClient.PresenceUpdated -= OnPresenceUpdatedAsync;
            _discordClient.Ready -= OnDiscordReadyAsync;
        }
    }

    /// <summary>
    /// Establishes connection to the MQTT broker using application configuration settings.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token to monitor for task cancellation.</param>
    /// <returns>A task tracking the asynchronous connection operation.</returns>
    private async Task ConnectMqttClientAsync(CancellationToken cancellationToken)
    {
        if (_mqttClient.IsConnected)
        {
            return;
        }

        string host = _configuration["Mqtt:Host"] ?? "localhost";
        int port = _configuration.GetValue<int>("Mqtt:Port", 1883);

        var optionsBuilder = new MqttClientOptionsBuilder()
            .WithTcpServer(host, port);

        string? username = _configuration["Mqtt:Username"];
        string? password = _configuration["Mqtt:Password"];

        if (!string.IsNullOrWhiteSpace(username))
        {
            optionsBuilder.WithCredentials(username, password);
        }

        try
        {
            await _mqttClient.ConnectAsync(optionsBuilder.Build(), cancellationToken);
            _logger.LogInformation("Successfully connected to MQTT broker at {Host}:{Port}", host, port);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to connect to MQTT broker at {Host}:{Port}", host, port);
        }
    }

    /// <summary>
    /// Performs an initial presence sweep for all cached guild users upon Discord client ready state.
    /// </summary>
    /// <returns>A task tracking asynchronous sweep processing.</returns>
    private async Task OnDiscordReadyAsync()
    {
        _logger.LogInformation("Discord client ready. Running initial user presence sweep for Home Assistant...");

        foreach (var guild in _discordClient.Guilds)
        {
            foreach (var user in guild.Users)
            {
                if (user.IsBot)
                {
                    continue;
                }

                await ProcessUserPresenceAsync(user, null);
            }
        }
    }

    /// <summary>
    /// Processes incoming user presence changes and publishes updated state payloads to MQTT.
    /// </summary>
    /// <param name="user">The user whose presence state was updated.</param>
    /// <param name="before">The user's prior presence snapshot.</param>
    /// <param name="after">The user's current presence snapshot.</param>
    /// <returns>A task tracking event processing and MQTT publishing operations.</returns>
    private async Task OnPresenceUpdatedAsync(SocketUser user, SocketPresence before, SocketPresence after)
    {
        if (user.IsBot)
        {
            return;
        }

        await ProcessUserPresenceAsync(user, after);
    }

    /// <summary>
    /// Extracts presence entity data, ensures Home Assistant discovery registration, and publishes current state to MQTT.
    /// </summary>
    /// <param name="user">The socket user target.</param>
    /// <param name="presence">The active presence object containing status and activities, if available.</param>
    /// <returns>A task tracking discovery and state publishing operations.</returns>
    private async Task ProcessUserPresenceAsync(SocketUser user, SocketPresence? presence = null)
    {
        try
        {
            var presenceEntity = ExtractPresenceEntity(user, presence);

            if (!_discoveredUsers.ContainsKey(user.Id))
            {
                await PublishHomeAssistantDiscoveryAsync(presenceEntity);
                _discoveredUsers.TryAdd(user.Id, true);
            }

            await PublishPresenceEntityAsync(presenceEntity);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process presence update for user {Username} ({UserId})", user.Username, user.Id);
        }
    }

    /// <summary>
    /// Transforms Discord user state into a structured entity model using either event presence or cached user state.
    /// </summary>
    /// <param name="user">The user object associated with the update event or guild sweep.</param>
    /// <param name="presence">The optional user presence data from a presence update event.</param>
    /// <returns>A populated user presence entity ready for JSON serialization.</returns>
    private static UserPresenceEntity ExtractPresenceEntity(SocketUser user, SocketPresence? presence = null)
    {
        var status = presence?.Status.ToString() ?? user.Status.ToString();
        var activities = presence?.Activities ?? user.Activities;

        var entity = new UserPresenceEntity
        {
            Username = user.Username,
            DiscordUserId = user.Id,
            Status = status,
            AvatarUrl = user.GetDisplayAvatarUrl(ImageFormat.Auto, 256),
            LastUpdated = DateTimeOffset.UtcNow
        };

        if (activities == null)
        {
            return entity;
        }

        var richGame = activities.OfType<RichGame>().FirstOrDefault();
        if (richGame != null)
        {
            entity.GameName = richGame.Name;
            entity.GameDetails = richGame.Details;
            entity.GameThumbnailUrl = ResolveGameThumbnailUrl(richGame);
            return entity;
        }

        var customStatus = activities.OfType<CustomStatusGame>().FirstOrDefault();
        if (customStatus != null)
        {
            entity.GameDetails = customStatus.State;
        }

        var standardGame = activities.OfType<Game>().FirstOrDefault();
        if (standardGame != null)
        {
            entity.GameName = standardGame.Name;
        }

        return entity;
    }

    /// <summary>
    /// Resolves direct URLs for game art or rich presence application assets.
    /// </summary>
    /// <param name="richGame">The rich presence game activity instance.</param>
    /// <returns>The resolved image URL string, or null if no image asset is attached.</returns>
    private static string? ResolveGameThumbnailUrl(RichGame richGame)
    {
        if (richGame.LargeAsset != null && !string.IsNullOrWhiteSpace(richGame.LargeAsset.ImageId))
        {
            return BuildImageUrl(richGame.ApplicationId, richGame.LargeAsset.ImageId);
        }

        if (richGame.SmallAsset != null && !string.IsNullOrWhiteSpace(richGame.SmallAsset.ImageId))
        {
            return BuildImageUrl(richGame.ApplicationId, richGame.SmallAsset.ImageId);
        }

        return null;
    }

    /// <summary>
    /// Constructs a valid image URL for Discord Application assets.
    /// </summary>
    /// <param name="applicationId">The rich presence application ID.</param>
    /// <param name="imageId">The image identifier from the asset.</param>
    /// <returns>A formatted URL string pointing to the image payload.</returns>
    private static string? BuildImageUrl(ulong? applicationId, string imageId)
    {
        if (imageId.StartsWith("mp:external/"))
        {
            return $"https://media.discordapp.net/{imageId.Replace("mp:", string.Empty)}";
        }

        if (applicationId.HasValue)
        {
            return $"https://cdn.discordapp.com/app-assets/{applicationId.Value}/{imageId}.png";
        }

        return null;
    }

    /// <summary>
    /// Transmits a single Home Assistant MQTT Auto-Discovery configuration payload to dynamically register a unified user presence entity with all metadata attributes.
    /// </summary>
    /// <param name="entity">The presence entity instance containing user metadata.</param>
    /// <returns>A task tracking asynchronous MQTT discovery publication.</returns>
    private async Task PublishHomeAssistantDiscoveryAsync(UserPresenceEntity entity)
    {
        if (!_mqttClient.IsConnected)
        {
            await ConnectMqttClientAsync(CancellationToken.None);
            if (!_mqttClient.IsConnected)
            {
                return;
            }
        }

        string deviceId = $"discord_user_{entity.DiscordUserId}";
        string stateTopic = $"wk7bot/presence/users/{entity.DiscordUserId}";

        var singleEntityDiscovery = new
        {
            name = $"{entity.Username} Presence",
            unique_id = deviceId,
            state_topic = stateTopic,
            value_template = "{{ value_json.Status }}",
            json_attributes_topic = stateTopic,
            icon = "mdi:discord",
            device = new
            {
                identifiers = new[] { deviceId },
                name = $"{entity.Username} (Discord)",
                model = "Discord Presence Tracker",
                manufacturer = "WK7Bot"
            }
        };

        await SendMqttDiscoveryPayloadAsync($"homeassistant/sensor/{deviceId}/config", singleEntityDiscovery);

        _logger.LogInformation("Published unified Home Assistant MQTT Discovery configuration entity for user {Username}", entity.Username);
    }

    /// <summary>
    /// Helper method for serializing and publishing individual discovery payload objects to the Home Assistant configuration topic.
    /// </summary>
    /// <param name="topic">The target discovery topic path.</param>
    /// <param name="payloadObject">The object payload to serialize as JSON.</param>
    /// <returns>A task tracking asynchronous publication.</returns>
    private async Task SendMqttDiscoveryPayloadAsync(string topic, object payloadObject)
    {
        string payloadJson = JsonSerializer.Serialize(payloadObject, new JsonSerializerOptions { WriteIndented = false });

        var message = new MqttApplicationMessageBuilder()
            .WithTopic(topic)
            .WithPayload(payloadJson)
            .WithRetainFlag(true)
            .Build();

        await _mqttClient.PublishAsync(message);
    }

    /// <summary>
    /// Serializes the presence entity, dynamically appends the entity_picture mapping for Home Assistant, and transmits it to the dedicated MQTT state topic.
    /// </summary>
    /// <param name="entity">The presence entity instance containing user data.</param>
    /// <returns>A task tracking asynchronous MQTT publication.</returns>
    private async Task PublishPresenceEntityAsync(UserPresenceEntity entity)
    {
        if (!_mqttClient.IsConnected)
        {
            await ConnectMqttClientAsync(CancellationToken.None);
            if (!_mqttClient.IsConnected)
            {
                _logger.LogWarning("MQTT client disconnected. Skipping presence broadcast for {Username}", entity.Username);
                return;
            }
        }

        string topic = $"wk7bot/presence/users/{entity.DiscordUserId}";

        var payloadObj = new
        {
            entity.Username,
            entity.DiscordUserId,
            entity.Status,
            entity.AvatarUrl,
            entity.GameName,
            entity.GameDetails,
            entity.GameThumbnailUrl,
            entity.LastUpdated,
            entity_picture = !string.IsNullOrWhiteSpace(entity.GameThumbnailUrl) 
                ? entity.GameThumbnailUrl 
                : entity.AvatarUrl
        };

        string payload = JsonSerializer.Serialize(payloadObj, new JsonSerializerOptions
        {
            WriteIndented = false
        });

        var message = new MqttApplicationMessageBuilder()
            .WithTopic(topic)
            .WithPayload(payload)
            .WithRetainFlag(true)
            .Build();

        await _mqttClient.PublishAsync(message);
        _logger.LogDebug("Published presence state for {Username} to MQTT topic {Topic}", entity.Username, topic);
    }
}