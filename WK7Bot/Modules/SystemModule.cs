using Discord;
using Discord.Interactions;
using Microsoft.Extensions.Logging;
using WK7Bot.Infrastructure.Data;
using WK7Bot.Services.Interfaces;

namespace WK7Bot.Modules;

/// <summary>
/// Provides general diagnostic slash commands for testing gateway latency, API connections, and command usage metrics.
/// </summary>
public class SystemModule : InteractionModuleBase<SocketInteractionContext>
{
    private readonly IHomeAssistantService _homeAssistantService;
    private readonly CommandUsageRepository _commandUsageRepository;
    private readonly ILogger<SystemModule> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="SystemModule"/> class.
    /// </summary>
    /// <param name="homeAssistantService">The Home Assistant API client service.</param>
    /// <param name="commandUsageRepository">The repository for command usage metrics.</param>
    /// <param name="logger">The logger instance for diagnostics.</param>
    public SystemModule(
        IHomeAssistantService homeAssistantService,
        CommandUsageRepository commandUsageRepository,
        ILogger<SystemModule> logger)
    {
        _homeAssistantService = homeAssistantService ?? throw new ArgumentNullException(nameof(homeAssistantService));
        _commandUsageRepository = commandUsageRepository ?? throw new ArgumentNullException(nameof(commandUsageRepository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Returns the active gateway WebSocket latency in milliseconds.
    /// </summary>
    /// <returns>A task representing the command execution operation.</returns>
    [SlashCommand("ping", "Zeigt die aktuelle Gateway-Latenz an.")]
    public async Task PingCommandAsync()
    {
        int latency = Context.Client.Latency;
        await RespondAsync($"Pong! Gateway-Latenz: {latency} ms.", ephemeral: true);
    }

    /// <summary>
    /// Queries the internal Home Assistant REST API to verify authentication and network access.
    /// </summary>
    /// <returns>A task representing the command execution operation.</returns>
    [SlashCommand("ha-status", "Testet die Verbindung zur internen Home-Assistant-API.")]
    public async Task HomeAssistantStatusCommandAsync()
    {
        // Defer first: the Supervisor round-trip can take longer than Discord's 3-second
        // initial-response window, which would otherwise fail the interaction entirely.
        await DeferAsync(ephemeral: true);

        try
        {
            string? response = await _homeAssistantService.GetApiStateAsync("config");

            if (response != null)
            {
                await FollowupAsync("✅ Home-Assistant-API über Supervisor-Token erfolgreich erreicht!", ephemeral: true);
            }
            else
            {
                await FollowupAsync("❌ Home-Assistant-API nicht erreichbar. Bitte prüfe die Supervisor-Logs.", ephemeral: true);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Error occurred while testing the Home Assistant API connection.");
            await FollowupAsync("❌ Home-Assistant-API nicht erreichbar. Bitte prüfe die Supervisor-Logs.", ephemeral: true);
        }
    }

    /// <summary>
    /// Returns the most frequently used slash commands in the last 7 days.
    /// </summary>
    /// <returns>A task representing the command execution operation.</returns>
    [SlashCommand("stats", "Zeigt die am häufigsten verwendeten Befehle der letzten 7 Tage.")]
    public async Task StatsCommandAsync()
    {
        await DeferAsync(ephemeral: true);

        try
        {
            var topCommands = await _commandUsageRepository.GetTopCommandsAsync(
                days: 7, top: 10, CancellationToken.None);

            if (topCommands.Count == 0)
            {
                await FollowupAsync("Keine Befehlsnutzung in den letzten 7 Tagen aufgezeichnet.", ephemeral: true);
                return;
            }

            var embed = new EmbedBuilder()
                .WithTitle("📊 Befehlsnutzung (letzte 7 Tage)")
                .WithColor(Color.Blue)
                .WithTimestamp(DateTimeOffset.UtcNow)
                .WithFooter("WK7 Bot • Nutzungsstatistik");

            var total = topCommands.Sum(x => x.Count);
            var position = 1;

            foreach (var item in topCommands)
            {
                var percentage = total > 0 ? (item.Count * 100.0 / total) : 0;
                embed.AddField(
                    $"#{position} /{item.CommandName}",
                    $"{item.Count}x ({percentage:F1}%)",
                    inline: true);
                position++;
            }

            embed.WithDescription($"Gesamt: {total} Befehlsaufrufe");

            await FollowupAsync(embed: embed.Build(), ephemeral: true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred while fetching command usage stats.");
            await FollowupAsync("❌ Statistiken konnten nicht geladen werden.", ephemeral: true);
        }
    }
}
