namespace WK7Bot.Services;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Discord.WebSocket;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MQTTnet;
using WK7Bot.Core.Utilities;
using WK7Bot.Options;

/// <summary>
/// Publishes the bot's own status to Home Assistant over MQTT discovery: a retained availability topic
/// guarded by a last-will testament (so a killed process reports OFFLINE without any graceful shutdown),
/// a duration sensor carrying uptime/version/Discord attributes, and per-feature "enabled" and "problem"
/// binary sensors backed by the <see cref="FeatureHealthTracker"/>. The entities make automations such
/// as "bot unreachable for 5 minutes → notify my phone" possible without the bot posting anything to
/// Discord. Discovery payloads and state values are republished after every broker (re)connect.
/// </summary>
public sealed class BotStatusMqttService : BackgroundService
{
    /// <summary>Heartbeat of the status sensor; must stay well below <see cref="BotStatusEntities.StatusExpireAfterSeconds"/>.</summary>
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(30);

    private readonly DiscordSocketClient _discordClient;
    private readonly IMqttClient _mqttClient;
    private readonly MqttConnectionCoordinator _coordinator;
    private readonly FeatureHealthTracker _health;
    private readonly Wk7BotOptions _options;
    private readonly ILogger<BotStatusMqttService> _logger;

    private readonly DateTime _startedAtUtc = DateTime.UtcNow;

    /// <summary>Last payload published per topic, so a heartbeat only republishes values that changed.</summary>
    private readonly Dictionary<string, string> _lastPayloads = new(StringComparer.Ordinal);

    private CancellationToken _stoppingToken = CancellationToken.None;

    /// <summary>
    /// Initializes a new instance of the <see cref="BotStatusMqttService"/> class.
    /// </summary>
    /// <param name="discordClient">The Discord socket client whose connection state is reported.</param>
    /// <param name="mqttClient">The shared MQTT client instance.</param>
    /// <param name="coordinator">The shared MQTT connection coordinator owning connect/reconnect behavior.</param>
    /// <param name="health">The feature health tracker providing the per-feature state.</param>
    /// <param name="options">The strongly-typed application configuration options.</param>
    /// <param name="logger">The logger instance.</param>
    public BotStatusMqttService(
        DiscordSocketClient discordClient,
        IMqttClient mqttClient,
        MqttConnectionCoordinator coordinator,
        FeatureHealthTracker health,
        IOptions<Wk7BotOptions> options,
        ILogger<BotStatusMqttService> logger)
    {
        _discordClient = discordClient ?? throw new ArgumentNullException(nameof(discordClient));
        _mqttClient = mqttClient ?? throw new ArgumentNullException(nameof(mqttClient));
        _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
        _health = health ?? throw new ArgumentNullException(nameof(health));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    }

    /// <summary>
    /// Arms the availability last-will on the shared connection, registers for post-connect
    /// republication and starts the heartbeat loop.
    /// </summary>
    /// <param name="stoppingToken">Cancellation token monitored for service shutdown.</param>
    /// <returns>A task representing the background execution lifecycle.</returns>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Features.MqttBotStatusEnabled)
        {
            _logger.LogInformation("MQTT bot status service is disabled via feature options.");
            return;
        }

        _stoppingToken = stoppingToken;

        // Armed before the shared connection is (re-)established so every connection attempt carries the
        // will: an ungraceful process death then reports OFFLINE without relying on our heartbeat.
        _coordinator.LastWill = (BotStatusEntities.AvailabilityTopic, BotStatusEntities.AvailabilityLostPayload);
        _coordinator.ConnectionEstablishedAsync += OnMqttConnectionEstablishedAsync;

        await _coordinator.StartMaintainingAsync(stoppingToken);

        // Another hosted MQTT service starts earlier and may already have established the shared
        // connection by the time this service subscribes: ConnectionEstablishedAsync for the
        // initial connect has then already fired, so catch up here. Without this the discovery
        // payloads and the availability ONLINE marker stay unpublished until a broker reconnect,
        // leaving sensor.wk7bot_status permanently unavailable in Home Assistant.
        if (_mqttClient.IsConnected)
        {
            await OnMqttConnectionEstablishedAsync();
        }

        using var timer = new PeriodicTimer(HeartbeatInterval);

        do
        {
            await PublishStatesAsync(force: false);
        }
        while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken));
    }

    /// <summary>
    /// Detaches the connection handler and publishes OFFLINE on a graceful shutdown — the broker does
    /// not fire the last will when the client disconnects cleanly.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token indicating service shutdown.</param>
    /// <returns>A task representing the asynchronous stop operation.</returns>
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _coordinator.ConnectionEstablishedAsync -= OnMqttConnectionEstablishedAsync;

        if (_options.Features.MqttBotStatusEnabled)
        {
            try
            {
                await PublishRetainedAsync(BotStatusEntities.AvailabilityTopic, BotStatusEntities.AvailabilityLostPayload);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not publish the OFFLINE availability payload during shutdown.");
            }
        }

        await base.StopAsync(cancellationToken);
    }

    /// <summary>
    /// Runs after every successful MQTT (re)connect: republishes the retained availability, the discovery
    /// payloads (a broker restart may have dropped them) and every current state value.
    /// </summary>
    /// <returns>A task representing the post-connect publication.</returns>
    private async Task OnMqttConnectionEstablishedAsync()
    {
        try
        {
            if (!await _coordinator.EnsureConnectedAsync(_stoppingToken))
            {
                return;
            }

            await PublishRetainedAsync(BotStatusEntities.AvailabilityTopic, BotStatusEntities.AvailabilityOnlinePayload);
            await RegisterEntitiesAsync();
            await PublishStatesAsync(force: true);
        }
        catch (OperationCanceledException) when (_stoppingToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not publish the bot status entities after the MQTT (re)connect.");
        }
    }

    /// <summary>
    /// Converges every status topic to its current value. On a heartbeat only values that changed since
    /// the last publication are republished; <paramref name="force"/> republishes everything (after a
    /// broker reconnect) and rebuilds the diff cache.
    /// </summary>
    /// <param name="force">Whether to republish all values regardless of the last published payload.</param>
    /// <returns>A task representing the state publication.</returns>
    private async Task PublishStatesAsync(bool force)
    {
        if (force)
        {
            _lastPayloads.Clear();
        }

        try
        {
            if (!await _coordinator.EnsureConnectedAsync(_stoppingToken))
            {
                return;
            }

            foreach (var (topic, payload) in BuildDesiredStates())
            {
                if (force || !_lastPayloads.TryGetValue(topic, out var last) || last != payload)
                {
                    await PublishRetainedAsync(topic, payload);
                    _lastPayloads[topic] = payload;
                }
            }
        }
        catch (OperationCanceledException) when (_stoppingToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not publish the bot status values.");
        }
    }

    /// <summary>
    /// Builds the topic to payload map of every status value: availability, the bot status sensor and,
    /// per monitored feature, the enabled state, the problem state and its attributes.
    /// </summary>
    /// <returns>The desired state values.</returns>
    private Dictionary<string, string> BuildDesiredStates()
    {
        var uptime = DateTime.UtcNow - _startedAtUtc;
        var monitored = BotStatusEntities.MonitoredFeatures;

        var desired = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [BotStatusEntities.AvailabilityTopic] = BotStatusEntities.AvailabilityOnlinePayload,
            [BotStatusEntities.StatusStateTopic] = BotStatusEntities.StatusStatePayload(uptime),
            [BotStatusEntities.StatusAttributesTopic] = BotStatusEntities.StatusAttributesPayload(
                new BotStatusAttributes(
                    AppInformation.Version,
                    _startedAtUtc,
                    (long)uptime.TotalSeconds,
                    _discordClient.ConnectionState.ToString(),
                    _discordClient.Guilds.Count,
                    monitored.Count,
                    monitored.Count(feature => _health.Get(feature.FeatureKey).IsProblem)))
        };

        foreach (var feature in monitored)
        {
            desired[BotStatusEntities.EnabledStateTopic(feature)] =
                BotStatusEntities.EnabledStatePayload(_options.Features.IsEnabled(feature.FeatureKey));

            var snapshot = _health.Get(feature.FeatureKey);
            desired[BotStatusEntities.ProblemStateTopic(feature)] = BotStatusEntities.ProblemStatePayload(snapshot);
            desired[BotStatusEntities.ProblemAttributesTopic(feature)] = BotStatusEntities.ProblemAttributesPayload(snapshot);
        }

        return desired;
    }

    /// <summary>
    /// Publishes the discovery payloads for the status sensor and each feature's enabled/problem sensors.
    /// Retained payloads survive broker restarts and are idempotent to republish.
    /// </summary>
    /// <returns>A task representing the discovery registration.</returns>
    private async Task RegisterEntitiesAsync()
    {
        var version = AppInformation.Version;

        await PublishRetainedAsync(
            BotStatusEntities.DiscoveryTopic("sensor", "status"),
            BotStatusEntities.StatusDiscoveryConfig(version));

        foreach (var feature in BotStatusEntities.MonitoredFeatures)
        {
            await PublishRetainedAsync(
                BotStatusEntities.DiscoveryTopic("binary_sensor", $"{feature.Slug}_enabled"),
                BotStatusEntities.EnabledDiscoveryConfig(feature, version));

            await PublishRetainedAsync(
                BotStatusEntities.DiscoveryTopic("binary_sensor", $"{feature.Slug}_problem"),
                BotStatusEntities.ProblemDiscoveryConfig(feature, version));
        }
    }

    /// <summary>
    /// Publishes one retained payload, logging (not throwing) a broker failure so a single failed
    /// publish cannot kill the heartbeat loop.
    /// </summary>
    /// <param name="topic">The topic to publish to.</param>
    /// <param name="payload">The retained payload.</param>
    /// <returns>A task representing the publication.</returns>
    private async Task PublishRetainedAsync(string topic, string payload)
    {
        try
        {
            await _mqttClient.PublishRetainedAsync(topic, payload);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Publishing the bot status topic {Topic} failed.", topic);
        }
    }
}
