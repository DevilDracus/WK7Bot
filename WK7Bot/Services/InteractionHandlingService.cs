using System.Reflection;
using Discord.Interactions;
using Discord.WebSocket;

namespace WK7Bot.Services;

/// <summary>
/// Manages the registration of interaction modules and routes incoming Discord slash command interactions.
/// </summary>
public class InteractionHandlingService : IHostedService
{
    private readonly DiscordSocketClient _client;
    private readonly InteractionService _interactionService;
    private readonly IServiceProvider _services;
    private readonly IConfiguration _configuration;
    private readonly ILogger<InteractionHandlingService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="InteractionHandlingService"/> class.
    /// </summary>
    /// <param name="client">The active Discord socket client instance.</param>
    /// <param name="interactionService">The interaction service responsible for handling commands.</param>
    /// <param name="services">The dependency injection service provider context.</param>
    /// <param name="configuration">The application configuration instance.</param>
    /// <param name="logger">The logging service instance.</param>
    public InteractionHandlingService(
        DiscordSocketClient client,
        InteractionService interactionService,
        IServiceProvider services,
        IConfiguration configuration,
        ILogger<InteractionHandlingService> logger)
    {
        _client = client;
        _interactionService = interactionService;
        _services = services;
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>
    /// Subscribes to gateway events and registers all interaction modules present in the executing assembly.
    /// </summary>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A task representing the asynchronous service start operation.</returns>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _client.Ready += OnClientReadyAsync;
        _client.InteractionCreated += OnInteractionCreatedAsync;

        await _interactionService.AddModulesAsync(Assembly.GetEntryAssembly(), _services);
    }

    /// <summary>
    /// Unsubscribes from gateway events upon service shutdown.
    /// </summary>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A task representing the asynchronous service stop operation.</returns>
    public Task StopAsync(CancellationToken cancellationToken)
    {
        _client.Ready -= OnClientReadyAsync;
        _client.InteractionCreated -= OnInteractionCreatedAsync;

        return Task.CompletedTask;
    }

    /// <summary>
    /// Registers interaction commands with the Discord API once the socket client reports ready.
    /// </summary>
    /// <returns>A task representing the asynchronous command registration operation.</returns>
    private async Task OnClientReadyAsync()
    {
        try
        {
            string? testGuildIdString = _configuration["Discord:TestGuildId"] ?? _configuration["TestGuildId"];

            if (ulong.TryParse(testGuildIdString, out ulong testGuildId))
            {
                await _interactionService.RegisterCommandsToGuildAsync(testGuildId);
                _logger.LogInformation("Successfully registered slash commands to guild {GuildId}.", testGuildId);
            }
            else
            {
                await _interactionService.RegisterCommandsGloballyAsync();
                _logger.LogInformation("Successfully registered slash commands globally.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to register interaction commands with Discord API.");
        }
    }

    /// <summary>
    /// Intercepts incoming WebSocket interactions and routes them to the matching command module handler.
    /// </summary>
    /// <param name="interaction">The incoming socket interaction payload from Discord.</param>
    /// <returns>A task representing the asynchronous interaction execution operation.</returns>
    private async Task OnInteractionCreatedAsync(SocketInteraction interaction)
    {
        try
        {
            var context = new SocketInteractionContext(_client, interaction);
            var result = await _interactionService.ExecuteCommandAsync(context, _services);

            if (!result.IsSuccess)
            {
                _logger.LogWarning("Interaction execution failed: {Reason}", result.ErrorReason);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An unhandled exception occurred while executing interaction.");
        }
    }
}