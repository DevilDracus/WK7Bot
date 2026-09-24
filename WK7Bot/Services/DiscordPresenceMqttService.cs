namespace WK7Bot.Services;

using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MQTTnet;
using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WK7Bot.Models;

/// <summary>
/// Listens to Discord presence updates, extracts user status and activity artwork, and publishes custom user entities over MQTT.
/// </summary>
public class DiscordPresenceMqttService : BackgroundService
{
    private readonly DiscordSocketClient _discordClient;
    private readonly IMqttClient _mqttClient;
    private readonly ILogger<DiscordPresenceMqttService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="DiscordPresenceMqttService"/> class.
    /// </summary>
    /// <param name="discordClient">The active Discord socket client handling server connections.</param>
    /// <param name="mqttClient">The connected MQTT client instance responsible for broker communication.</param>
    /// <param name="logger">The logging service instance for operational diagnostics.</param>
    public DiscordPresenceMqttService(DiscordSocketClient discordClient, IMqttClient mqttClient, ILogger<DiscordPresenceMqttService> logger)
    {
        _discordClient = discordClient ?? throw new ArgumentNullException(nameof(discordClient));
        _mqttClient = mqttClient ?? throw new ArgumentNullException(nameof(mqttClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Registers presence update handlers and maintains background execution until shutdown is requested.
    /// </summary>
    /// <param name="stoppingToken">Cancellation token monitored for background service termination.</param>
    /// <returns>A task representing the asynchronous lifecycle of the service.</returns>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _discordClient.PresenceUpdated += OnPresenceUpdatedAsync;

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            
        }
        finally
        {
            _discordClient.PresenceUpdated -= OnPresenceUpdatedAsync;
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

        try
        {
            var presenceEntity = ExtractPresenceEntity(user, after);
            await PublishPresenceEntityAsync(presenceEntity);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process presence update for user {Username} ({UserId})", user.Username, user.Id);
        }
    }

    /// <summary>
    /// Transforms Discord user and presence state into a structured entity model.
    /// </summary>
    /// <param name="user">The user object associated with the update event.</param>
    /// <param name="presence">The user presence data containing activities and status.</param>
    /// <returns>A populated user presence entity ready for JSON serialization.</returns>
    private static UserPresenceEntity ExtractPresenceEntity(SocketUser user, SocketPresence presence)
    {
        var entity = new UserPresenceEntity
        {
            Username = user.Username,
            DiscordUserId = user.Id,
            Status = presence.Status.ToString(),
            AvatarUrl = user.GetDisplayAvatarUrl(ImageFormat.Auto, 256),
            LastUpdated = DateTimeOffset.UtcNow
        };

        var richGame = presence.Activities.OfType<RichGame>().FirstOrDefault();
        if (richGame != null)
        {
            entity.GameName = richGame.Name;
            entity.GameDetails = richGame.Details;
            entity.GameThumbnailUrl = ResolveGameThumbnailUrl(richGame);
            return entity;
        }

        var customStatus = presence.Activities.OfType<CustomStatusGame>().FirstOrDefault();
        if (customStatus != null)
        {
            entity.GameDetails = customStatus.State;
        }

        var standardGame = presence.Activities.OfType<Game>().FirstOrDefault();
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
    /// Serializes the presence entity and transmits it to the dedicated MQTT state topic.
    /// </summary>
    /// <param name="entity">The presence entity instance containing user data.</param>
    /// <returns>A task tracking asynchronous MQTT publication.</returns>
    private async Task PublishPresenceEntityAsync(UserPresenceEntity entity)
    {
        if (!_mqttClient.IsConnected)
        {
            _logger.LogWarning("MQTT client disconnected. Skipping presence broadcast for {Username}", entity.Username);
            return;
        }

        string topic = $"wk7bot/presence/users/{entity.DiscordUserId}";
        string payload = JsonSerializer.Serialize(entity, new JsonSerializerOptions
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