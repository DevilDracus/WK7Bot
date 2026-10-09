using Discord.Interactions;
using WK7Bot.Services.Interfaces;

namespace WK7Bot.Modules;

/// <summary>
/// Provides general diagnostic slash commands for testing gateway latency and API connections.
/// </summary>
public class SystemModule : InteractionModuleBase<SocketInteractionContext>
{
    private readonly IHomeAssistantService _homeAssistantService;

    /// <summary>
    /// Initializes a new instance of the <see cref="SystemModule"/> class.
    /// </summary>
    /// <param name="homeAssistantService">The Home Assistant API client service.</param>
    public SystemModule(IHomeAssistantService homeAssistantService)
    {
        _homeAssistantService = homeAssistantService;
    }

    /// <summary>
    /// Returns the active gateway WebSocket latency in milliseconds.
    /// </summary>
    /// <returns>A task representing the command execution operation.</returns>
    [SlashCommand("ping", "Checks current gateway latency.")]
    public async Task PingCommandAsync()
    {
        int latency = Context.Client.Latency;
        await RespondAsync($"Pong! Gateway latency is {latency} ms.", ephemeral: true);
    }

    /// <summary>
    /// Queries the internal Home Assistant REST API to verify authentication and network access.
    /// </summary>
    /// <returns>A task representing the command execution operation.</returns>
    [SlashCommand("ha-status", "Tests communication with internal Home Assistant API.")]
    public async Task HomeAssistantStatusCommandAsync()
    {
        // Defer first: the Supervisor round-trip can take longer than Discord's 3-second
        // initial-response window, which would otherwise fail the interaction entirely.
        await DeferAsync(ephemeral: true);

        string? response = await _homeAssistantService.GetApiStateAsync("config");

        if (response != null)
        {
            await FollowupAsync("Successfully connected to Home Assistant API via Supervisor token!", ephemeral: true);
        }
        else
        {
            await FollowupAsync("Failed to reach Home Assistant API. Verify Supervisor logs.", ephemeral: true);
        }
    }
}