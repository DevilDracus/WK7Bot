namespace WK7Bot.Services;

using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

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
    /// Set once the host begins stopping the service. Interactions that arrive after this point are
    /// ignored instead of being executed against a DI provider that is about to be disposed.
    /// </summary>
    private volatile bool _isStopping;

    /// <summary>
    /// Initializes a new instance of the <see cref="InteractionHandlingService"/> class.
    /// </summary>
    /// <param name="client">The active Discord socket client instance.</param>
    /// <param name="interactionService">The interaction service responsible for handling commands.</param>
    /// <param name="services">The dependency injection service provider context.</param>
    /// <param name="configuration">The application configuration instance used to retrieve target guild options.</param>
    /// <param name="logger">The logging service instance.</param>
    public InteractionHandlingService(
        DiscordSocketClient client,
        InteractionService interactionService,
        IServiceProvider services,
        IConfiguration configuration,
        ILogger<InteractionHandlingService> logger)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _interactionService = interactionService ?? throw new ArgumentNullException(nameof(interactionService));
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
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
        _interactionService.InteractionExecuted += OnInteractionExecutedAsync;

        await _interactionService.AddModulesAsync(Assembly.GetEntryAssembly(), _services);
    }

    /// <summary>
    /// Unsubscribes from gateway events upon service shutdown.
    /// </summary>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A task representing the asynchronous service stop operation.</returns>
    public Task StopAsync(CancellationToken cancellationToken)
    {
        // Flip the shutdown flag first so interactions that arrive while the host is tearing down
        // are dropped instead of executing against a DI provider that is about to be disposed.
        _isStopping = true;
        _client.Ready -= OnClientReadyAsync;
        _client.InteractionCreated -= OnInteractionCreatedAsync;
        _interactionService.InteractionExecuted -= OnInteractionExecutedAsync;

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
            var testGuildIdString = _configuration["Discord:TestGuildId"] ?? _configuration["TestGuildId"];

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

            // Command registration is once-per-connect bookkeeping: detach so a gateway reconnect
            // (Ready fires again) does not re-register commands and burn rate limit on every reconnect.
            _client.Ready -= OnClientReadyAsync;
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
            if (_isStopping)
            {
                // The host is shutting down: the root service provider may already be disposed, so
                // executing the interaction would fatal. Drop it instead.
                return;
            }

            var context = new SocketInteractionContext(_client, interaction);

            // The root provider is passed, NOT a handler-owned scope: InteractionService keeps its
            // default RunMode.Async, in which ExecuteCommandAsync returns as soon as the command is
            // dispatched to a detached task. A scope disposed together with this handler would tear
            // the command's services down while it is still running — Discord.NET's own
            // AutoServiceScopes CreateScope then throws ObjectDisposedException, the command never
            // runs, and no response is ever sent (Discord reports "Die Anwendung reagiert nicht",
            // and the detached faulted task surfaces as UnobservedTaskException). With
            // AutoServiceScopes enabled, every command execution resolves its own scope from the
            // provider passed here, so scoped services (the DbContext) are per-execution, not shared.
            var result = await _interactionService.ExecuteCommandAsync(context, _services);

            // A non-exception failure here means the command never ran: a failed precondition
            // ([RequireUserPermission]/[RequireGuild]), an unknown command or a parse error all
            // return as the immediate result. Nothing answered the interaction in those cases, so
            // Discord would show "Die Anwendung reagiert nicht" — answer it explicitly instead.
            // Exception results are logged by OnInteractionExecutedAsync, which owns that path.
            if (!result.IsSuccess && result is not ExecuteResult { Exception: { } })
            {
                _logger.LogWarning("Interaction execution failed: {Reason}", result.ErrorReason);
                await ReportUnansweredInteractionAsync(context.Interaction, result.ErrorReason);
            }
        }
        catch (ObjectDisposedException)
        {
            // Shutdown won the race: the interaction arrived after the service provider was disposed
            // (root provider disposal or an in-flight scope disposed by host teardown). Dropping the
            // interaction is the correct outcome during shutdown — logging at error level here would
            // feed the error-notification queue with shutdown noise.
            _logger.LogDebug("Dropped an interaction because the service provider was disposed during shutdown.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An unhandled exception occurred while executing interaction.");
        }
    }

    /// <summary>
    /// Surfaces uncaught command-handler exceptions: Discord.Net reports them through
    /// <see cref="InteractionService.InteractionExecuted"/> as an <see cref="ExecuteResult"/> carrying
    /// the original exception, while <see cref="OnInteractionCreatedAsync"/> only sees a terse warning
    /// reason. Logging here at error level routes them into the error-notification pipeline with
    /// their stack trace; the interaction is answered when the failing command left it unanswered.
    /// </summary>
    /// <param name="commandInfo">The executed command; <see langword="null"/> for unmatched interactions.</param>
    /// <param name="context">The interaction context.</param>
    /// <param name="result">The final execution result.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    private async Task OnInteractionExecutedAsync(ICommandInfo commandInfo, IInteractionContext context, IResult result)
    {
        if (result is ExecuteResult { Exception: { } exception })
        {
            _logger.LogError(exception, "Unhandled exception in interaction command {CommandName}: {Reason}", commandInfo?.Name ?? "unknown", result.ErrorReason);

            // A command that throws before deferring/responding leaves the interaction unanswered,
            // which Discord reports as "Die Anwendung reagiert nicht". Answer it here; the check
            // inside keeps this safe when the command had already answered before failing.
            await ReportUnansweredInteractionAsync(context.Interaction, "Bei der Ausführung des Befehls ist ein Fehler aufgetreten.");
        }
    }

    /// <summary>
    /// Sends an ephemeral error reply to an interaction that was never answered. Discord requires a
    /// response within three seconds, so any path that skips one leaves the user staring at
    /// "Die Anwendung reagiert nicht" with no clue what happened.
    /// </summary>
    /// <param name="interaction">The interaction to answer.</param>
    /// <param name="reason">The reason to show; <see langword="null"/> or blank yields a generic message.</param>
    /// <returns>A task representing the asynchronous reply operation.</returns>
    private async Task ReportUnansweredInteractionAsync(IDiscordInteraction interaction, string? reason)
    {
        // Failing to answer must never surface as a second exception: this runs from error paths
        // where throwing would only replace the original failure.
        try
        {
            var message = string.IsNullOrWhiteSpace(reason)
                ? "❌ Der Befehl konnte nicht ausgeführt werden."
                : $"❌ {reason}";

            // HasResponded is set once the interaction was answered or deferred. When it was only
            // deferred there is no reply text yet, so a follow-up carries the error without
            // colliding with an existing response.
            if (interaction.HasResponded)
            {
                await interaction.FollowupAsync(message, ephemeral: true);
            }
            else
            {
                await interaction.RespondAsync(message, ephemeral: true);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not answer the failed interaction with reason {Reason}.", reason);
        }
    }
}