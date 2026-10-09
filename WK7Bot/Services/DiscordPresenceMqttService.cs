namespace WK7Bot.Services;

using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MQTTnet;
using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WK7Bot.Core.Utilities;
using WK7Bot.Models;
using WK7Bot.Options;
using WK7Bot.Services.Interfaces;

/// <summary>
/// Listens to Discord presence updates, extracts user status and activity artwork, and publishes unified user entities to Home Assistant via MQTT Discovery.
/// </summary>
public class DiscordPresenceMqttService : BackgroundService
{
    private readonly DiscordSocketClient _discordClient;
    private readonly IMqttClient _mqttClient;
    private readonly MqttConnectionCoordinator _coordinator;
    private readonly ISteamService _steamService;
    private readonly SteamDataCache _steamDataCache;
    private readonly IBattleNetService _battleNetService;
    private readonly BattleNetDataCache _battleNetDataCache;
    private readonly Wk7BotOptions _options;
    private readonly ILogger<DiscordPresenceMqttService> _logger;
    private readonly ConcurrentDictionary<ulong, bool> _discoveredUsers = new();

    private static readonly TimeSpan SteamFetchCooldown = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan BattleNetFetchCooldown = TimeSpan.FromSeconds(60);

    /// <summary>
    /// How often mapped users are re-fetched from Steam/Battle.net and republished, independent of Discord
    /// presence events. Without this sweep, playtime and achievement progress would only refresh when the
    /// user's Discord presence changed (status/game switch) or the bot restarted.
    /// </summary>
    private static readonly TimeSpan PresenceRefreshInterval = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Initializes a new instance of the <see cref="DiscordPresenceMqttService"/> class.
    /// </summary>
    /// <param name="discordClient">The active Discord socket client handling server connections.</param>
    /// <param name="mqttClient">The connected MQTT client instance responsible for broker communication.</param>
    /// <param name="coordinator">The shared MQTT connection coordinator owning connect/reconnect behavior.</param>
    /// <param name="steamService">The Steam service instance used to fetch Steam metadata.</param>
    /// <param name="battleNetService">The Battle.net service instance used to fetch Battle.net profile data.</param>
    /// <param name="options">The strongly-typed application configuration options.</param>
    /// <param name="logger">The logging service instance for operational diagnostics.</param>
    public DiscordPresenceMqttService(
        DiscordSocketClient discordClient,
        IMqttClient mqttClient,
        MqttConnectionCoordinator coordinator,
        ISteamService steamService,
        IBattleNetService battleNetService,
        IOptions<Wk7BotOptions> options,
        ILogger<DiscordPresenceMqttService> logger)
    {
        _discordClient = discordClient ?? throw new ArgumentNullException(nameof(discordClient));
        _mqttClient = mqttClient ?? throw new ArgumentNullException(nameof(mqttClient));
        _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
        _steamService = steamService ?? throw new ArgumentNullException(nameof(steamService));
        _steamDataCache = new SteamDataCache(steamService, SteamFetchCooldown);
        _battleNetService = battleNetService ?? throw new ArgumentNullException(nameof(battleNetService));
        _battleNetDataCache = new BattleNetDataCache(battleNetService, BattleNetFetchCooldown);
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    }

    /// <summary>
    /// Registers presence update handlers, connects the MQTT client, and maintains background execution.
    /// </summary>
    /// <param name="stoppingToken">Cancellation token monitored for background service termination.</param>
    /// <returns>A task representing the asynchronous lifecycle of the service.</returns>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Features.DiscordPresenceMqttEnabled)
        {
            _logger.LogInformation("Discord presence MQTT service is disabled via feature options.");
            return;
        }

        await _coordinator.StartMaintainingAsync(stoppingToken);

        _discordClient.PresenceUpdated += OnPresenceUpdatedAsync;
        _discordClient.Ready += OnDiscordReadyAsync;

        // After a broker reconnect the retained discovery configs are gone; clearing the marker
        // makes the next presence update re-publish them for every user.
        _coordinator.ConnectionEstablishedAsync += OnMqttConnectionEstablishedAsync;

        try
        {
            using var refreshTimer = new PeriodicTimer(PresenceRefreshInterval);
            while (await refreshTimer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    await RefreshMappedPresenceAsync();
                }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                {
                    _logger.LogError(ex, "Periodic mapped-user presence refresh failed; it will retry on the next tick.");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Host is shutting down cleanly
        }
        finally
        {
            _discordClient.PresenceUpdated -= OnPresenceUpdatedAsync;
            _discordClient.Ready -= OnDiscordReadyAsync;
            _coordinator.ConnectionEstablishedAsync -= OnMqttConnectionEstablishedAsync;
        }
    }

    /// <summary>
    /// Clears the per-user discovery markers after a broker (re)connect so the Home Assistant
    /// discovery configurations are re-published for every user.
    /// </summary>
    /// <returns>A completed task.</returns>
    private Task OnMqttConnectionEstablishedAsync()
    {
        _discoveredUsers.Clear();
        _logger.LogInformation("MQTT connection established; presence discovery configurations will be re-published.");
        return Task.CompletedTask;
    }

    /// <summary>
    /// Republishes enriched presence state for every guild user with an enabled Steam or Battle.net mapping,
    /// bypassing the short-lived data caches so playtime and achievement progress advance even when Discord
    /// fires no presence change events during a game session.
    /// </summary>
    /// <returns>A task tracking the refresh sweep.</returns>
    private async Task RefreshMappedPresenceAsync()
    {
        await _discordClient.ForEachGuildUserAsync(
            HasExternalMapping,
            user => ProcessUserPresenceAsync(user, null, forceRefresh: true));
    }

    /// <summary>
    /// Determines whether the user has a Steam or Battle.net mapping whose feature is enabled, i.e. whether
    /// enriched external data would be attached to their presence state.
    /// </summary>
    /// <param name="user">The guild user to check.</param>
    /// <returns><see langword="true"/> when a matching enabled mapping exists; otherwise <see langword="false"/>.</returns>
    private bool HasExternalMapping(SocketGuildUser user)
    {
        string userId = user.Id.ToString();

        if (_options.Features.SteamPresenceEnabled
            && !string.IsNullOrWhiteSpace(_steamService.GetSteamIdForDiscordUser(userId)))
        {
            return true;
        }

        return _options.Features.BattleNetPresenceEnabled
            && !string.IsNullOrWhiteSpace(_battleNetService.GetMappingForDiscordUser(userId)?.RefreshToken);
    }

    /// <summary>
    /// Performs an initial presence sweep for all cached guild users upon Discord client ready state.
    /// </summary>
    /// <returns>A task representing asynchronous sweep processing.</returns>
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
    /// Extracts presence entity data, enriches Steam and Battle.net telemetry if enabled, ensures Home Assistant discovery registration, and publishes current state to MQTT.
    /// </summary>
    /// <param name="user">The socket user target.</param>
    /// <param name="presence">The active presence object containing status and activities, if available.</param>
    /// <param name="forceRefresh">When true, still-fresh Steam/Battle.net cache entries are bypassed so the periodic refresh sweep publishes current data.</param>
    /// <returns>A task tracking discovery and state publishing operations.</returns>
    private async Task ProcessUserPresenceAsync(SocketUser user, SocketPresence? presence = null, bool forceRefresh = false)
    {
        try
        {
            var presenceEntity = ExtractPresenceEntity(user, presence);

            if (_options.Features.SteamPresenceEnabled)
            {
                string? userSteamId = _steamService.GetSteamIdForDiscordUser(user.Id.ToString());
                if (!string.IsNullOrWhiteSpace(userSteamId))
                {
                    presenceEntity.SteamData = await _steamDataCache.GetSteamUserDataAsync(userSteamId, forceRefresh);
                }
            }

            if (_options.Features.BattleNetPresenceEnabled)
            {
                var battleNetMapping = _battleNetService.GetMappingForDiscordUser(user.Id.ToString());
                if (!string.IsNullOrWhiteSpace(battleNetMapping?.RefreshToken))
                {
                    presenceEntity.BattleNetData = await _battleNetDataCache.GetBattleNetUserDataAsync(battleNetMapping.RefreshToken, forceRefresh);
                }
            }

            if (!_discoveredUsers.ContainsKey(user.Id))
            {
                if (await PublishHomeAssistantDiscoveryAsync(presenceEntity))
                {
                    _discoveredUsers.TryAdd(user.Id, true);
                }
            }

            await PublishPresenceEntityAsync(presenceEntity);
        }
        catch (Exception ex)
        {
            // Stable message text: user-specific placeholders would give every failing user its own
            // error signature and flood the error-DM channel with one message per user.
            _logger.LogError(ex, "Failed to process presence update for a mapped user.");
            _logger.LogDebug("Presence update failure details for user {Username} ({UserId}).", user.Username, user.Id);
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
            return $"https://media.discordapp.net/{imageId.Substring("mp:".Length)}";
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
    /// <returns><see langword="true"/> when the discovery payload was published; <see langword="false"/> when MQTT was unavailable, in which case the user is retried on the next update.</returns>
    private async Task<bool> PublishHomeAssistantDiscoveryAsync(UserPresenceEntity entity)
    {
        if (!_mqttClient.IsConnected)
        {
            if (!await _coordinator.EnsureConnectedAsync(CancellationToken.None))
            {
                return false;
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
        return true;
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

        await _mqttClient.PublishRetainedAsync(topic, payloadJson);
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
            if (!await _coordinator.EnsureConnectedAsync(CancellationToken.None))
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
            CurrentGameTitle = entity.SteamData?.CurrentGameTitle,
            CurrentGameAppId = entity.SteamData?.CurrentGameAppId,
            PlaytimeLastTwoWeeksMinutes = entity.SteamData?.PlaytimeLastTwoWeeksMinutes,
            PlaytimeDisplay = entity.SteamData?.PlaytimeDisplay,
            PersonaState = entity.SteamData?.PersonaState,
            SteamAvatarUrl = entity.SteamData?.SteamAvatarUrl,
            entity.SteamData,
            BattleTag = entity.BattleNetData?.BattleTag,
            BattleNetAvatarUrl = entity.BattleNetData?.AvatarUrl,
            WoWMainCharacter = entity.BattleNetData?.MainCharacterDisplay,
            entity.BattleNetData,
            entity.LastUpdated,
            entity_picture = !string.IsNullOrWhiteSpace(entity.GameThumbnailUrl) 
                ? entity.GameThumbnailUrl 
                : entity.AvatarUrl
        };

        string payload = JsonSerializer.Serialize(payloadObj, new JsonSerializerOptions
        {
            WriteIndented = false
        });

        await _mqttClient.PublishRetainedAsync(topic, payload);
        _logger.LogDebug("Published presence state for {Username} to MQTT topic {Topic}", entity.Username, topic);
    }
}