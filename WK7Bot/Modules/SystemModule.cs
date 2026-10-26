using Discord;
using Discord.Interactions;
using Microsoft.Extensions.Logging;
using System.Text;
using WK7Bot.Core.Utilities;
using WK7Bot.Infrastructure.Data;
using WK7Bot.Services.Interfaces;

using WK7Bot.Core.Interfaces;

namespace WK7Bot.Modules;

/// <summary>
/// Provides general diagnostic slash commands for testing gateway latency, API connections, command usage metrics,
/// feature health, and bot help.
/// </summary>
public class SystemModule : BotModuleBase
{
    private readonly IHomeAssistantService _homeAssistantService;
    private readonly ICommandUsageRepository _commandUsageRepository;
    private readonly IFeatureHealthTracker _healthTracker;
    private readonly IExternalDependencyHealthChecker _dependencyHealthChecker;
    private readonly CommandRateLimiter _rateLimiter;
    private readonly ILogger<SystemModule> _logger;

    /// <summary>
    /// Gets the logger used for error reporting.
    /// </summary>
    protected override ILogger Logger => _logger;

    private const string HomeAssistantStatusCommandKey = "ha-status";
    private const string HealthCommandKey = "health";

    private static readonly TimeSpan HomeAssistantStatusCooldown = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan HealthCooldown = TimeSpan.FromSeconds(15);

    /// <summary>
    /// Initializes a new instance of the <see cref="SystemModule"/> class.
    /// </summary>
    /// <param name="homeAssistantService">The Home Assistant API client service.</param>
    /// <param name="commandUsageRepository">The repository for command usage metrics.</param>
    /// <param name="healthTracker">The feature health tracker.</param>
    /// <param name="dependencyHealthChecker">The external dependency health checker.</param>
    /// <param name="rateLimiter">Per-user command cooldown guard.</param>
    /// <param name="logger">The logging service instance for diagnostics.</param>
    public SystemModule(
        IHomeAssistantService homeAssistantService,
        ICommandUsageRepository commandUsageRepository,
        IFeatureHealthTracker healthTracker,
        IExternalDependencyHealthChecker dependencyHealthChecker,
        CommandRateLimiter rateLimiter,
        ILogger<SystemModule> logger)
    {
        _homeAssistantService = homeAssistantService ?? throw new ArgumentNullException(nameof(homeAssistantService));
        _commandUsageRepository = commandUsageRepository ?? throw new ArgumentNullException(nameof(commandUsageRepository));
        _healthTracker = healthTracker ?? throw new ArgumentNullException(nameof(healthTracker));
        _dependencyHealthChecker = dependencyHealthChecker ?? throw new ArgumentNullException(nameof(dependencyHealthChecker));
        _rateLimiter = rateLimiter ?? throw new ArgumentNullException(nameof(rateLimiter));
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
        if (!_rateLimiter.CanExecute(Context.User.Id, HomeAssistantStatusCommandKey, HomeAssistantStatusCooldown))
        {
            await RespondAsync("⏳ Bitte warte 15 Sekunden, bevor du den Home-Assistant-Status erneut prüfst.", ephemeral: true);
            return;
        }

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

            await FollowupAsync(components: CardBuilder.FromEmbed(embed.Build()), flags: MessageFlags.ComponentsV2, ephemeral: true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred while fetching command usage stats.");
            await FollowupAsync("❌ Statistiken konnten nicht geladen werden.", ephemeral: true);
        }
    }

    /// <summary>
    /// Returns the health status of all monitored background services.
    /// </summary>
    [SlashCommand("health", "Zeigt den Status aller Hintergrunddienste und externen Abhängigkeiten an.")]
    public async Task HealthCommandAsync()
    {
        if (!_rateLimiter.CanExecute(Context.User.Id, HealthCommandKey, HealthCooldown))
        {
            await RespondAsync("⏳ Bitte warte 15 Sekunden, bevor du den Dienst-Status erneut abrufst.", ephemeral: true);
            return;
        }

        await DeferAsync(ephemeral: true);

        try
        {
            var states = _healthTracker.GetAll();
            var dependencies = await _dependencyHealthChecker.CheckAllAsync(CancellationToken.None).ConfigureAwait(false);

            var embed = new EmbedBuilder()
                .WithTitle("🩺 Dienst-Status")
                .WithColor(Color.Green)
                .WithTimestamp(DateTimeOffset.UtcNow)
                .WithFooter("WK7 Bot • Feature Health");

            // --- Hintergrunddienste ---
            if (states.Count > 0)
            {
                var servicesBuilder = new StringBuilder();
                foreach (var (feature, snapshot) in states.OrderBy(s => s.Key))
                {
                    var statusEmoji = snapshot.IsProblem ? "🔴" : (snapshot.HasRun ? "🟢" : "⚪");
                    var lastSuccess = snapshot.LastSuccessUtc.HasValue
                        ? $"<t:{new DateTimeOffset(snapshot.LastSuccessUtc.Value).ToUnixTimeSeconds()}:R>"
                        : "nie";
                    var lastError = snapshot.IsProblem && !string.IsNullOrWhiteSpace(snapshot.LastError)
                        ? $" — ⚠️ {snapshot.LastError}"
                        : string.Empty;

                    servicesBuilder.AppendLine($"{statusEmoji} **{feature}** — Letzter Erfolg: {lastSuccess}{lastError}");
                }

                embed.AddField("⚙️ Hintergrunddienste", servicesBuilder.ToString().TrimEnd(), inline: false);
            }
            else
            {
                embed.AddField("⚙️ Hintergrunddienste", "Noch keine Gesundheitsdaten verfügbar. Die Dienste starten gerade.", inline: false);
            }

            // --- Externe Abhängigkeiten ---
            var depsBuilder = new StringBuilder();
            foreach (var dep in dependencies.OrderBy(d => d.Name))
            {
                var emoji = dep.IsHealthy ? "✅" : "❌";
                var latency = dep.Latency.HasValue ? $" ({dep.Latency.Value.TotalMilliseconds:F0} ms)" : string.Empty;
                depsBuilder.AppendLine($"{emoji} **{dep.Name}** — {dep.Detail}{latency}");
            }

            embed.AddField("🌐 Externe Abhängigkeiten", depsBuilder.ToString().TrimEnd(), inline: false);

            await FollowupAsync(components: CardBuilder.FromEmbed(embed.Build()), flags: MessageFlags.ComponentsV2, ephemeral: true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred while fetching health status.");
            await FollowupAsync("❌ Gesundheitsdaten konnten nicht geladen werden.", ephemeral: true);
        }
    }

    /// <summary>
    /// Displays a categorized list of all available slash commands.
    /// </summary>
    [SlashCommand("help", "Zeigt alle verfügbaren Befehle an.")]
    public async Task HelpCommandAsync()
    {
        try
        {
            var embed = new EmbedBuilder()
                .WithTitle("📖 WK7 Bot Befehle")
                .WithColor(Color.Teal)
                .WithDescription("Hier sind alle verfügbaren Slash-Befehle:")
                .WithTimestamp(DateTimeOffset.UtcNow)
                .WithFooter("WK7 Bot • Hilfe");

            var systemCommands = new[] { "ping", "ha-status", "stats", "health", "help" };
            var infoCommands = new[] { "check-waste", "spontan-treff" };
            var foodCommands = new[] { "recipe", "recipe-seasonal", "recipe-generate", "seasonal-produce", "check-food" };
            var rssCommands = new[] { "rss", "rss add", "rss list", "rss remove", "rss dashboard" };
            var dealCommands = new[] { "deals" };

            AddCommandField(embed, "🖥️ System", systemCommands);
            AddCommandField(embed, "📰 Info", infoCommands);
            AddCommandField(embed, "🍽️ Essen", foodCommands);
            AddCommandField(embed, "📡 RSS", rssCommands);
            AddCommandField(embed, "🎮 Deals", dealCommands);

            await RespondAsync(components: CardBuilder.FromEmbed(embed.Build()), flags: MessageFlags.ComponentsV2, ephemeral: true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred while building help output.");
            await RespondAsync("❌ Hilfe konnte nicht geladen werden.", ephemeral: true);
        }
    }

    private static void AddCommandField(EmbedBuilder embed, string category, string[] commands)
    {
        if (commands.Length == 0)
            return;

        var text = string.Join("\n", commands.Select(c => $"• `/{c}`"));
        embed.AddField(category, text, inline: true);
    }
}
