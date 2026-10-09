namespace WK7Bot.Services;

using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MQTTnet;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WK7Bot.Core.Utilities;
using WK7Bot.Options;

/// <summary>
/// Background service that bridges Discord text channels and direct messages with Home Assistant via MQTT Discovery and notification commands.
/// </summary>
public class HomeAssistantNotifierService : BackgroundService
{
    private readonly DiscordSocketClient _discordClient;
    private readonly IMqttClient _mqttClient;
    private readonly MqttConnectionCoordinator _coordinator;
    private readonly Wk7BotOptions _options;
    private readonly ILogger<HomeAssistantNotifierService> _logger;

    private CancellationToken _stoppingToken = CancellationToken.None;

    /// <summary>
    /// Initializes a new instance of the <see cref="HomeAssistantNotifierService"/> class with required dependencies.
    /// </summary>
    /// <param name="discordClient">The active Discord socket client instance used for channel and user interactions.</param>
    /// <param name="mqttClient">The active MQTT client instance used to communicate with Home Assistant.</param>
    /// <param name="coordinator">The shared MQTT connection coordinator owning connect/reconnect behavior.</param>
    /// <param name="options">The strongly-typed application configuration options.</param>
    /// <param name="logger">The diagnostic logging service instance.</param>
    public HomeAssistantNotifierService(
        DiscordSocketClient discordClient, 
        IMqttClient mqttClient,
        MqttConnectionCoordinator coordinator,
        IOptions<Wk7BotOptions> options,
        ILogger<HomeAssistantNotifierService> logger)
    {
        _discordClient = discordClient ?? throw new ArgumentNullException(nameof(discordClient));
        _mqttClient = mqttClient ?? throw new ArgumentNullException(nameof(mqttClient));
        _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    }

    /// <summary>
    /// Wires MQTT notification handling, Discord entity tracking, and the shared MQTT connection lifecycle:
    /// every successful (re)connect (re)subscribes the notification topic and re-registers discovery entities,
    /// so a broker restart no longer silently kills Home Assistant notifications.
    /// </summary>
    /// <param name="stoppingToken">A cancellation token monitored to observe service shutdown requests.</param>
    /// <returns>A task representing the background execution lifecycle.</returns>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Features.HomeAssistantNotifierEnabled)
        {
            _logger.LogInformation("Home Assistant notifier service is disabled via feature options.");
            return;
        }

        _stoppingToken = stoppingToken;

        _mqttClient.ApplicationMessageReceivedAsync += ProcessIncomingNotificationAsync;
        _coordinator.ConnectionEstablishedAsync += OnMqttConnectionEstablishedAsync;

        _discordClient.Ready += OnReadyAsync;
        _discordClient.ChannelCreated += OnChannelCreatedAsync;
        _discordClient.ChannelDestroyed += OnChannelDestroyedAsync;
        _discordClient.ChannelUpdated += OnChannelUpdatedAsync;

        await _coordinator.StartMaintainingAsync(stoppingToken);
    }

    /// <summary>
    /// Detaches every Discord and MQTT handler on shutdown so a stopped service keeps no references
    /// to the singleton clients.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token indicating service shutdown.</param>
    /// <returns>A task representing the asynchronous stop operation.</returns>
    public override Task StopAsync(CancellationToken cancellationToken)
    {
        _mqttClient.ApplicationMessageReceivedAsync -= ProcessIncomingNotificationAsync;
        _coordinator.ConnectionEstablishedAsync -= OnMqttConnectionEstablishedAsync;

        _discordClient.Ready -= OnReadyAsync;
        _discordClient.ChannelCreated -= OnChannelCreatedAsync;
        _discordClient.ChannelDestroyed -= OnChannelDestroyedAsync;
        _discordClient.ChannelUpdated -= OnChannelUpdatedAsync;

        return base.StopAsync(cancellationToken);
    }

    /// <summary>
    /// Runs after every successful MQTT (re)connect: subscribes to the notification command topic and
    /// re-registers all discovery entities (retained payloads cover broker restarts, re-registration covers a
    /// fresh broker).
    /// </summary>
    /// <returns>A task representing the subscription and re-registration.</returns>
    private async Task OnMqttConnectionEstablishedAsync()
    {
        try
        {
            await _mqttClient.SubscribeAsync("homeassistant/notify/+/set", cancellationToken: _stoppingToken);
        }
        catch (OperationCanceledException) when (_stoppingToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to subscribe to the MQTT notification topic; Home Assistant notifications will not work until the next reconnect.");
            return;
        }

        if (_discordClient.ConnectionState == ConnectionState.Connected)
        {
            await RegisterAllEntitiesAsync();
        }
    }

    /// <summary>
    /// Event handler executed when the Discord client achieves a ready state, initiating full channel and user entity discovery.
    /// </summary>
    /// <returns>A task representing the initial entity discovery operation.</returns>
    private async Task OnReadyAsync()
    {
        try
        {
            await RegisterAllEntitiesAsync();
        }
        catch (OperationCanceledException) when (_stoppingToken.IsCancellationRequested)
        {
            // Host shutdown during registration.
        }
        catch (Exception ex)
        {
            // Registration runs again on the next MQTT connect, so a failure here must not escape the event.
            _logger.LogError(ex, "Entity registration after Discord ready failed; it will retry on the next MQTT connect.");
        }
    }

    /// <summary>
    /// Event handler executed when a new channel is created within a Discord guild.
    /// </summary>
    /// <param name="channel">The newly created socket channel instance.</param>
    /// <returns>A task representing the conditional entity registration.</returns>
    private async Task OnChannelCreatedAsync(SocketChannel channel)
    {
        if (channel is SocketTextChannel textChannel && IsWritable(textChannel))
        {
            try
            {
                await RegisterChannelNotificationEntityAsync(textChannel);
            }
            catch (OperationCanceledException) when (_stoppingToken.IsCancellationRequested)
            {
                // Host shutdown during registration.
            }
            catch (Exception ex)
            {
                // Registration re-runs on the next MQTT connect; an escaping exception would only
                // disturb Discord.Net's gateway event dispatch.
                _logger.LogError(ex, "Registering the notification entity for a newly created channel failed; it is re-registered on the next MQTT connect.");
            }
        }
    }

    /// <summary>
    /// Event handler executed when an existing channel is deleted from a Discord guild.
    /// </summary>
    /// <param name="channel">The deleted socket channel instance.</param>
    /// <returns>A task representing the entity removal operation.</returns>
    private async Task OnChannelDestroyedAsync(SocketChannel channel)
    {
        if (channel is SocketTextChannel textChannel)
        {
            try
            {
                await UnregisterChannelNotificationEntityAsync(textChannel);
            }
            catch (OperationCanceledException) when (_stoppingToken.IsCancellationRequested)
            {
                // Host shutdown during unregistration.
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unregistering the notification entity for a deleted channel failed; it is re-registered on the next MQTT connect.");
            }
        }
    }

    /// <summary>
    /// Event handler executed when a channel's metadata or permissions are updated.
    /// </summary>
    /// <param name="oldChannel">The previous socket channel state.</param>
    /// <param name="newChannel">The updated socket channel state.</param>
    /// <returns>A task representing the state synchronization operation.</returns>
    private async Task OnChannelUpdatedAsync(SocketChannel oldChannel, SocketChannel newChannel)
    {
        if (newChannel is SocketTextChannel textChannel)
        {
            try
            {
                if (IsWritable(textChannel))
                {
                    await RegisterChannelNotificationEntityAsync(textChannel);
                }
                else
                {
                    await UnregisterChannelNotificationEntityAsync(textChannel);
                }
            }
            catch (OperationCanceledException) when (_stoppingToken.IsCancellationRequested)
            {
                // Host shutdown during synchronization.
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Synchronizing the notification entity of an updated channel failed; it is re-registered on the next MQTT connect.");
            }
        }
    }

    /// <summary>
    /// Iterates through all connected guilds and registers writable text channels alongside configured direct message user notification entities.
    /// </summary>
    /// <returns>A task representing the complete entity registration workflow.</returns>
    public async Task RegisterAllEntitiesAsync()
    {
        if (!_mqttClient.IsConnected)
        {
            _logger.LogInformation("MQTT is not connected; entity registration is deferred to the next connect.");
            return;
        }

        foreach (var guild in _discordClient.Guilds)
        {
            foreach (var channel in guild.TextChannels)
            {
                if (!IsWritable(channel))
                {
                    continue;
                }

                try
                {
                    await RegisterChannelNotificationEntityAsync(channel);
                }
                catch (OperationCanceledException) when (_stoppingToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    // Stable message text so identical failures collapse into one throttled notification.
                    _logger.LogError(ex, "Publishing MQTT discovery while registering channel entities failed; it will retry on the next connect.");
                }
            }
        }

        foreach (var userId in GetConfiguredDmUserIds())
        {
            try
            {
                await RegisterUserDmNotificationEntityAsync(userId);
            }
            catch (OperationCanceledException) when (_stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Publishing MQTT discovery while registering DM entities failed; it will retry on the next connect.");
            }
        }
    }

    /// <summary>
    /// Retrieves the list of configured user IDs designated to receive private direct message notifications from configuration options.
    /// </summary>
    /// <returns>A collection of parsed ulong user identifiers.</returns>
    private IEnumerable<ulong> GetConfiguredDmUserIds()
    {
        foreach (var idStr in _options.DiscordDmUserIds)
        {
            if (ulong.TryParse(idStr.Trim(), out var id))
            {
                yield return id;
            }
        }
    }

    /// <summary>
    /// Registers a specific Discord text channel as a Home Assistant notification entity via MQTT Discovery.
    /// </summary>
    /// <param name="channel">The target text channel to expose as an MQTT entity.</param>
    /// <returns>A task representing the publication of the discovery MQTT payload.</returns>
    private async Task RegisterChannelNotificationEntityAsync(SocketTextChannel channel)
    {
        var sanitizedChannelName = NameSanitizer.ToMqttSafeName(channel.Name);
        var uniqueId = $"wk7_notify_{sanitizedChannelName}_{channel.Id}";
        var discoveryTopic = $"homeassistant/notify/{uniqueId}/config";
        var commandTopic = $"homeassistant/notify/wk7_{channel.Id}/set";

        var discoveryPayload = new
        {
            name = $"Discord #{channel.Name} ({channel.Guild.Name})",
            unique_id = uniqueId,
            command_topic = commandTopic,
            device = new
            {
                identifiers = new[] { "csharp_discord_bot" },
                name = "Discord Bot Service",
                model = "C# Discord Integration",
                manufacturer = "Custom Application"
            }
        };

        var jsonPayload = JsonSerializer.Serialize(discoveryPayload);

        var message = new MqttApplicationMessageBuilder()
            .WithTopic(discoveryTopic)
            .WithPayload(jsonPayload)
            .WithRetainFlag()
            .Build();

        await _mqttClient.PublishAsync(message);
    }

    /// <summary>
    /// Registers a specific Discord user as a Home Assistant direct message notification entity via MQTT Discovery.
    /// </summary>
    /// <param name="userId">The target user Snowflake ID to expose as an MQTT entity.</param>
    /// <returns>A task representing the publication of the discovery MQTT payload.</returns>
    private async Task RegisterUserDmNotificationEntityAsync(ulong userId)
    {
        IUser? user = _discordClient.GetUser(userId);
        if (user == null)
        {
            user = await _discordClient.Rest.GetUserAsync(userId);
        }

        var username = user?.Username ?? userId.ToString();
        var sanitizedUsername = NameSanitizer.ToMqttSafeName(username);
        
        var uniqueId = $"wk7_notify_dm_{sanitizedUsername}_{userId}";
        var discoveryTopic = $"homeassistant/notify/{uniqueId}/config";
        var commandTopic = $"homeassistant/notify/wk7_dm_{userId}/set";

        var discoveryPayload = new
        {
            name = $"Discord DM (@{username})",
            unique_id = uniqueId,
            command_topic = commandTopic,
            device = new
            {
                identifiers = new[] { "csharp_discord_bot" },
                name = "Discord Bot Service",
                model = "C# Discord Integration",
                manufacturer = "Custom Application"
            }
        };

        var jsonPayload = JsonSerializer.Serialize(discoveryPayload);

        var message = new MqttApplicationMessageBuilder()
            .WithTopic(discoveryTopic)
            .WithPayload(jsonPayload)
            .WithRetainFlag()
            .Build();

        await _mqttClient.PublishAsync(message);
    }

    /// <summary>
    /// Removes a Discord channel entity from Home Assistant by publishing an empty payload to its discovery topic.
    /// </summary>
    /// <param name="channel">The target text channel to remove from Home Assistant discovery.</param>
    /// <returns>A task representing the publication of the MQTT deletion payload.</returns>
    private async Task UnregisterChannelNotificationEntityAsync(SocketTextChannel channel)
    {
        var sanitizedChannelName = NameSanitizer.ToMqttSafeName(channel.Name);
        var uniqueId = $"wk7_notify_{sanitizedChannelName}_{channel.Id}";
        var discoveryTopic = $"homeassistant/notify/{uniqueId}/config";

        var message = new MqttApplicationMessageBuilder()
            .WithTopic(discoveryTopic)
            .WithPayload(Array.Empty<byte>())
            .WithRetainFlag()
            .Build();

        await _mqttClient.PublishAsync(message);
    }

    /// <summary>
    /// Determines whether the current bot user possesses permissions to send messages in the specified channel.
    /// </summary>
    /// <param name="channel">The text channel to evaluate permissions against.</param>
    /// <returns>True if the bot can send messages in the channel; otherwise, false.</returns>
    private bool IsWritable(SocketTextChannel channel)
    {
        var permissions = channel.Guild.CurrentUser.GetPermissions(channel);
        return permissions.SendMessages;
    }

    /// <summary>
    /// Extracts target channel or user identifiers from incoming MQTT message topics and dispatches the message payload to Discord.
    /// </summary>
    /// <param name="eventArgs">The received MQTT application message event arguments containing topic and payload data.</param>
    /// <returns>A task representing the message dispatch operation.</returns>
    private async Task ProcessIncomingNotificationAsync(MqttApplicationMessageReceivedEventArgs eventArgs)
    {
        try
        {
            var topic = eventArgs.ApplicationMessage.Topic;
            var payloadText = eventArgs.ApplicationMessage.ConvertPayloadToString();

            var segments = topic.Split('/');
            if (segments.Length == 4 && segments[2].StartsWith("wk7_") && segments[3] == "set")
            {
                var identifierPart = segments[2].Substring("wk7_".Length);

                if (identifierPart.StartsWith("dm_"))
                {
                    var userIdString = identifierPart.Substring("dm_".Length);
                    if (ulong.TryParse(userIdString, out var userId))
                    {
                        IUser? user = _discordClient.GetUser(userId);
                        if (user == null)
                        {
                            user = await _discordClient.Rest.GetUserAsync(userId);
                        }

                        if (user != null)
                        {
                            var dmChannel = await user.CreateDMChannelAsync();
                            await dmChannel.SendMessageAsync(payloadText);
                        }
                    }
                }
                else if (ulong.TryParse(identifierPart, out var channelId))
                {
                    if (_discordClient.GetChannel(channelId) is SocketTextChannel textChannel)
                    {
                        await textChannel.SendMessageAsync(payloadText);
                    }
                }
            }
        }
        catch (OperationCanceledException) when (_stoppingToken.IsCancellationRequested)
        {
            // Host shutdown while dispatching.
        }
        catch (Exception ex)
        {
            // Stable message text so identical dispatch failures collapse into one throttled notification.
            _logger.LogError(ex, "Failed to dispatch an incoming MQTT notification to Discord.");
        }
    }
}