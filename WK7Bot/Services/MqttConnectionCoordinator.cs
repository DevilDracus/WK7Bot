namespace WK7Bot.Services;

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MQTTnet;
using WK7Bot.Options;

/// <summary>
/// Owns the single shared <see cref="IMqttClient"/> connection: serializes concurrent connect attempts from the
/// Home Assistant notifier and the presence publisher (MQTTnet forbids concurrent <c>ConnectAsync</c> calls),
/// verifies the broker's CONNACK result, keeps reconnecting with capped backoff after connection loss, and
/// raises <see cref="ConnectionEstablishedAsync"/> so subscribers can resubscribe and re-register their entities.
/// </summary>
public sealed class MqttConnectionCoordinator
{
    private static readonly TimeSpan InitialReconnectDelay = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan MaxReconnectDelay = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Upper bound for a single TCP connect attempt so an unreachable broker cannot stall the
    /// calling Discord gateway handler for the OS default (which can reach minutes).
    /// </summary>
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(15);

    private readonly IMqttClient _mqttClient;
    private readonly Wk7BotOptions _options;
    private readonly ILogger<MqttConnectionCoordinator> _logger;
    private readonly SemaphoreSlim _connectGate = new(1, 1);

    private int _maintaining;
    private int _reconnectRunning;
    private CancellationToken _stoppingToken = CancellationToken.None;
    private TimeSpan _reconnectDelay = InitialReconnectDelay;

    /// <summary>
    /// Initializes a new instance of the <see cref="MqttConnectionCoordinator"/> class.
    /// </summary>
    /// <param name="mqttClient">The shared MQTT client instance.</param>
    /// <param name="options">Application options instance.</param>
    /// <param name="logger">Logger instance.</param>
    public MqttConnectionCoordinator(
        IMqttClient mqttClient,
        IOptions<Wk7BotOptions> options,
        ILogger<MqttConnectionCoordinator> logger)
    {
        _mqttClient = mqttClient ?? throw new ArgumentNullException(nameof(mqttClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));

        Host = string.IsNullOrWhiteSpace(_options.MqttHost) ? "core-mosquitto" : _options.MqttHost;
        Port = _options.MqttPort > 0 ? _options.MqttPort : 1883;
    }

    /// <summary>Gets the resolved MQTT broker host.</summary>
    public string Host { get; }

    /// <summary>Gets the resolved MQTT broker port.</summary>
    public int Port { get; }

    /// <summary>
    /// Gets or sets the last-will testament included in every connection attempt: the broker publishes
    /// this retained payload on the topic when our connection drops without a clean disconnect (process
    /// kill, network loss). Set before the first connect so all connections carry the will.
    /// </summary>
    public (string Topic, string Payload)? LastWill { get; set; }

    /// <summary>
    /// Raised after every successful (re)connection so subscribers can resubscribe topics and re-publish
    /// retained discovery payloads. Handler failures are logged and never break the connection pipeline.
    /// </summary>
    public event Func<Task>? ConnectionEstablishedAsync;

    /// <summary>
    /// Attaches the connection event handlers (idempotent), performs one connection attempt, and starts the
    /// background reconnect loop when the broker is not reachable yet.
    /// </summary>
    /// <param name="stoppingToken">The host shutdown token observed by the reconnect loop.</param>
    /// <returns>A task representing the initial connection attempt.</returns>
    public async Task StartMaintainingAsync(CancellationToken stoppingToken)
    {
        if (Interlocked.Exchange(ref _maintaining, 1) == 1)
        {
            return;
        }

        _stoppingToken = stoppingToken;
        _mqttClient.ConnectedAsync += OnMqttConnectedAsync;
        _mqttClient.DisconnectedAsync += OnMqttDisconnectedAsync;

        if (!await EnsureConnectedAsync(stoppingToken))
        {
            _logger.LogWarning(
                "Initial MQTT connection to {Host}:{Port} failed; retrying in the background every {Delay} until the broker accepts us.",
                Host,
                Port,
                _reconnectDelay);
            StartReconnectLoop();
        }
    }

    /// <summary>
    /// Performs a single gated connection attempt. Safe to call concurrently from publish paths —
    /// only one attempt runs at a time and callers join the in-flight attempt's outcome.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token to observe.</param>
    /// <returns><see langword="true"/> when the client is connected afterwards.</returns>
    public async Task<bool> EnsureConnectedAsync(CancellationToken cancellationToken)
    {
        if (_mqttClient.IsConnected)
        {
            return true;
        }

        await _connectGate.WaitAsync(cancellationToken);
        try
        {
            if (_mqttClient.IsConnected)
            {
                return true;
            }

            var result = await _mqttClient.ConnectAsync(BuildOptions(), cancellationToken);
            if (result.ResultCode == MqttClientConnectResultCode.Success && _mqttClient.IsConnected)
            {
                return true;
            }

            _logger.LogWarning(
                "MQTT broker at {Host}:{Port} refused the connection ({ResultCode}).",
                Host,
                Port,
                result.ResultCode);
            return false;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "MQTT connection attempt to {Host}:{Port} failed.", Host, Port);
            return false;
        }
        finally
        {
            _connectGate.Release();
        }
    }

    /// <summary>
    /// Builds the connection options from the configured host, port, and credentials.
    /// </summary>
    private MqttClientOptions BuildOptions()
    {
        var optionsBuilder = new MqttClientOptionsBuilder()
            .WithTcpServer(Host, Port)
            .WithCleanSession()
            // Bound the TCP connect attempt: the OS default can be minutes long, which would
            // stall the calling Discord gateway handler on every publish while the broker is
            // unreachable.
            .WithTimeout(ConnectTimeout);

        if (!string.IsNullOrWhiteSpace(_options.MqttUsername))
        {
            optionsBuilder.WithCredentials(_options.MqttUsername, _options.MqttPassword);
        }

        if (LastWill is { } will)
        {
            optionsBuilder
                .WithWillTopic(will.Topic)
                .WithWillPayload(will.Payload)
                .WithWillRetain(true);
        }

        return optionsBuilder.Build();
    }

    /// <summary>
    /// Starts the single background reconnect loop (idempotent).
    /// </summary>
    private void StartReconnectLoop()
    {
        if (Interlocked.Exchange(ref _reconnectRunning, 1) == 1)
        {
            return;
        }

        _ = Task.Run(ReconnectLoopAsync);
    }

    /// <summary>
    /// Retries the connection with exponential backoff (5s doubling up to 5 minutes) until the broker accepts,
    /// the host shuts down, or another path already reconnected the client.
    /// </summary>
    private async Task ReconnectLoopAsync()
    {
        try
        {
            while (!_stoppingToken.IsCancellationRequested && !_mqttClient.IsConnected)
            {
                try
                {
                    await Task.Delay(_reconnectDelay, _stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                if (await EnsureConnectedAsync(_stoppingToken))
                {
                    return;
                }

                _reconnectDelay = TimeSpan.FromTicks(Math.Min(_reconnectDelay.Ticks * 2, MaxReconnectDelay.Ticks));
                _logger.LogInformation("Next MQTT reconnect attempt in {Delay}.", _reconnectDelay);
            }
        }
        catch (OperationCanceledException) when (_stoppingToken.IsCancellationRequested)
        {
            // Host shutdown while awaiting the gate or connecting.
        }
        finally
        {
            Volatile.Write(ref _reconnectRunning, 0);
        }
    }

    /// <summary>
    /// Logs the successful connection, resets the backoff, and notifies subscribers (resubscribe/registration).
    /// </summary>
    /// <param name="args">The MQTTnet connected event arguments.</param>
    /// <returns>A task representing the subscriber notification.</returns>
    private async Task OnMqttConnectedAsync(MqttClientConnectedEventArgs args)
    {
        _reconnectDelay = InitialReconnectDelay;
        _logger.LogInformation("Connected to MQTT broker at {Host}:{Port}.", Host, Port);

        try
        {
            if (ConnectionEstablishedAsync is { } notify)
            {
                await notify();
            }
        }
        catch (OperationCanceledException) when (_stoppingToken.IsCancellationRequested)
        {
            // Host shutdown during post-connect setup.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Post-connect MQTT setup failed; incoming notifications or discovery may not work.");
        }
    }

    /// <summary>
    /// Logs the connection loss and schedules the background reconnect loop.
    /// </summary>
    /// <param name="args">The MQTTnet disconnected event arguments.</param>
    /// <returns>A completed task.</returns>
    private Task OnMqttDisconnectedAsync(MqttClientDisconnectedEventArgs args)
    {
        if (_stoppingToken.IsCancellationRequested)
        {
            return Task.CompletedTask;
        }

        _logger.LogWarning(
            "MQTT connection to {Host}:{Port} lost ({Reason}); scheduling reconnect.",
            Host,
            Port,
            args.Reason);
        StartReconnectLoop();
        return Task.CompletedTask;
    }
}
