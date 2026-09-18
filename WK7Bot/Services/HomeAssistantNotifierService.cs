using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Discord.WebSocket;
using Microsoft.Extensions.Hosting;
using MQTTnet;

public class HomeAssistantNotifierService : BackgroundService
{
    private readonly DiscordSocketClient _discordClient;
    private readonly IMqttClient _mqttClient;

    public HomeAssistantNotifierService(DiscordSocketClient discordClient, IMqttClient mqttClient)
    {
        _discordClient = discordClient;
        _mqttClient = mqttClient;
    }

    /// <summary>
    /// Executes the background service, subscribing to Discord socket events and configuring MQTT listeners.
    /// </summary>
    /// <param name="stoppingToken">A cancellation token monitored to observe service shutdown requests.</param>
    /// <returns>A task representing the background execution lifecycle.</returns>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _discordClient.Ready += OnReadyAsync;
        _discordClient.ChannelCreated += OnChannelCreatedAsync;
        _discordClient.ChannelDestroyed += OnChannelDestroyedAsync;
        _discordClient.ChannelUpdated += OnChannelUpdatedAsync;

        await _mqttClient.SubscribeAsync("homeassistant/notify/wk7_+/set", cancellationToken: stoppingToken);

        _mqttClient.ApplicationMessageReceivedAsync += async eventArgs =>
        {
            await ProcessIncomingMultiChannelNotificationAsync(eventArgs);
        };

        await Task.CompletedTask;
    }

    /// <summary>
    /// Event handler executed when the Discord client achieves a ready state, initiating full channel discovery.
    /// </summary>
    /// <returns>A task representing the initial channel discovery operation.</returns>
    private async Task OnReadyAsync()
    {
        await RegisterAllWritableChannelsAsync();
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
            await RegisterChannelNotificationEntityAsync(textChannel);
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
            await UnregisterChannelNotificationEntityAsync(textChannel);
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
            if (IsWritable(textChannel))
            {
                await RegisterChannelNotificationEntityAsync(textChannel);
            }
            else
            {
                await UnregisterChannelNotificationEntityAsync(textChannel);
            }
        }
    }

    /// <summary>
    /// Iterates through all connected guilds and registers entities for text channels where send permissions are granted.
    /// </summary>
    /// <returns>A task representing the bulk channel registration workflow.</returns>
    public async Task RegisterAllWritableChannelsAsync()
    {
        foreach (var guild in _discordClient.Guilds)
        {
            foreach (var channel in guild.TextChannels)
            {
                if (IsWritable(channel))
                {
                    await RegisterChannelNotificationEntityAsync(channel);
                }
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
        var sanitizedChannelName = Regex.Replace(channel.Name.ToLowerInvariant(), @"[^a-z0-9_]", "_");
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
    /// Removes a Discord channel entity from Home Assistant by publishing an empty payload to its discovery topic.
    /// </summary>
    /// <param name="channel">The target text channel to remove from Home Assistant discovery.</param>
    /// <returns>A task representing the publication of the MQTT deletion payload.</returns>
    private async Task UnregisterChannelNotificationEntityAsync(SocketTextChannel channel)
    {
        var sanitizedChannelName = Regex.Replace(channel.Name.ToLowerInvariant(), @"[^a-z0-9_]", "_");
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
    /// Extracts the target channel identifier from an incoming MQTT message topic and dispatches the message payload to Discord.
    /// </summary>
    /// <param name="eventArgs">The received MQTT application message event arguments containing topic and payload data.</param>
    /// <returns>A task representing the message dispatch operation.</returns>
    private async Task ProcessIncomingMultiChannelNotificationAsync(MqttApplicationMessageReceivedEventArgs eventArgs)
    {
        var topic = eventArgs.ApplicationMessage.Topic;
        var payloadText = eventArgs.ApplicationMessage.ConvertPayloadToString();

        var segments = topic.Split('/');
        if (segments.Length == 4 && segments[2].StartsWith("wk7_") && segments[3] == "set")
        {
            var channelIdString = segments[2].Replace("wk7_", string.Empty);
            if (ulong.TryParse(channelIdString, out var channelId))
            {
                if (_discordClient.GetChannel(channelId) is SocketTextChannel textChannel)
                {
                    await textChannel.SendMessageAsync(payloadText);
                }
            }
        }
    }
}