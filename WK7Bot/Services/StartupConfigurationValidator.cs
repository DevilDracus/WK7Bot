namespace WK7Bot.Services;

using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Options;
using WK7Bot.Core.Utilities;
using WK7Bot.Options;

/// <summary>
/// Validates critical configuration on startup and posts a summary to the error notification channel.
/// Catches silent misconfigurations (missing API keys, empty postal codes, unreachable brokers)
/// before the background services start and silently fail.
/// </summary>
public class StartupConfigurationValidator : BackgroundService
{
    private readonly DiscordSocketClient _discordClient;
    private readonly Wk7BotOptions _options;
    private readonly MqttConnectionCoordinator _mqttCoordinator;
    private readonly ILogger<StartupConfigurationValidator> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="StartupConfigurationValidator"/> class.
    /// </summary>
    public StartupConfigurationValidator(
        DiscordSocketClient discordClient,
        IOptions<Wk7BotOptions> options,
        MqttConnectionCoordinator mqttCoordinator,
        ILogger<StartupConfigurationValidator> logger)
    {
        _discordClient = discordClient ?? throw new ArgumentNullException(nameof(discordClient));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _mqttCoordinator = mqttCoordinator ?? throw new ArgumentNullException(nameof(mqttCoordinator));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Waits for the Discord gateway, then validates configuration and posts the report only
    /// when there are actual problems or when verbose reporting is explicitly enabled.
    /// </summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            // Wait for the gateway so we can post to Discord.
            while (_discordClient.ConnectionState != ConnectionState.Connected && !stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken).ConfigureAwait(false);
            }

            if (stoppingToken.IsCancellationRequested)
            {
                return;
            }

            var report = BuildReport();

            // Always post a brief confirmation on successful start so the operator knows
            // the bot is alive. Full embed only when there are problems or verbose mode is on.
            if (report.AllValid && !_options.StartupReportVerbose)
            {
                await PostBriefConfirmationAsync(stoppingToken).ConfigureAwait(false);
                return;
            }

            await PostReportAsync(report, stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutdown before validation could run.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Startup configuration validator failed to post its report.");
        }
    }

    /// <summary>
    /// Builds the validation report by checking every critical configuration value.
    /// </summary>
    private ValidationReport BuildReport()
    {
        var report = new ValidationReport();

        // Discord token
        report.Add(
            "Discord Token",
            !string.IsNullOrWhiteSpace(_options.DiscordToken),
            "Required for bot login. Set via add-on configuration.");

        // DWD warnings
        if (_options.Features.DwdWarningEnabled)
        {
            report.Add(
                "DWD Postal Code",
                _options.DwdWarning.HasPostalCode(),
                "dwd_warning.postal_code is required for weather warnings. Service stays idle without it.");
        }

        // Gemini
        if (_options.Features.FoodServiceEnabled)
        {
            report.Add(
                "Gemini API Key",
                !string.IsNullOrWhiteSpace(_options.GeminiApiKey),
                "gemini_api_key is required for seasonal produce and recipe generation.");
        }

        // Steam
        if (_options.Features.SteamPresenceEnabled)
        {
            report.Add(
                "Steam API Key",
                !string.IsNullOrWhiteSpace(_options.SteamApiKey),
                "steam_api_key is required for Steam presence enrichment.");

            report.Add(
                "Discord ↔ Steam Mappings",
                _options.DiscordSteamMappings.Count > 0,
                "discord_steam_mappings must contain at least one entry for presence tracking.");
        }

        // Battle.net
        if (_options.Features.BattleNetPresenceEnabled)
        {
            report.Add(
                "Battle.net OAuth",
                !string.IsNullOrWhiteSpace(_options.BattleNetClientId) && !string.IsNullOrWhiteSpace(_options.BattleNetClientSecret),
                "battlenet_client_id and battlenet_client_secret are required for Battle.net integration.");
        }

        // MQTT
        report.Add(
            "MQTT Broker",
            !string.IsNullOrWhiteSpace(_options.MqttHost),
            "mqtt_host must point to a reachable broker (default: core-mosquitto).");

        // Error notifications
        if (_options.Features.ErrorNotificationsEnabled)
        {
            report.Add(
                "Error Notification Recipients",
                _options.ErrorNotifyUserIds.Count > 0,
                "error_notify_user_ids must contain at least one Discord user ID for error DMs.");
        }

        return report;
    }

    /// <summary>
    /// Posts a brief one-line confirmation that the bot started successfully.
    /// This gives the operator confidence without spamming a full embed on every restart.
    /// </summary>
    private async Task PostBriefConfirmationAsync(CancellationToken stoppingToken)
    {
        const string message = "✅ **WK7 Bot** started — all configuration items valid.";

        // Try to post to the first guild's system channel
        var guild = _discordClient.Guilds.FirstOrDefault();
        if (guild?.SystemChannel is not null)
        {
            try
            {
                await guild.SystemChannel.SendMessageAsync(message).ConfigureAwait(false);
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not post brief startup confirmation to guild system channel.");
            }
        }

        // Fallback: DM the first configured error user
        if (_options.ErrorNotifyUserIds.Count > 0 &&
            ulong.TryParse(_options.ErrorNotifyUserIds[0], out var userId))
        {
            try
            {
                var user = await _discordClient.GetUserAsync(userId).ConfigureAwait(false);
                if (user is not null)
                {
                    var dm = await user.CreateDMChannelAsync().ConfigureAwait(false);
                    await dm.SendMessageAsync(message).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not send brief startup confirmation via DM.");
            }
        }
    }

    /// <summary>
    /// Posts the validation report as an embed to the first available guild's system channel,
    /// or falls back to direct messages.
    /// </summary>
    private async Task PostReportAsync(ValidationReport report, CancellationToken stoppingToken)
    {
        var embed = new EmbedBuilder()
            .WithTitle("🔧 WK7 Bot Startup Configuration Report")
            .WithColor(report.AllValid ? Color.Green : Color.Orange)
            .WithTimestamp(DateTimeOffset.UtcNow)
            .WithFooter("WK7 Bot • Startup Validator");

        foreach (var item in report.Items)
        {
            var icon = item.IsValid ? "✅" : "❌";
            embed.AddField($"{icon} {item.Name}", item.HelpText, inline: true);
        }

        var summary = report.AllValid
            ? "All critical configuration items are valid. The bot should operate normally."
            : $"{report.InvalidCount} configuration item(s) need attention. The bot will start, but some features may be unavailable.";

        embed.WithDescription(summary);

        // Try to post to the first guild's system channel
        var guild = _discordClient.Guilds.FirstOrDefault();
        if (guild?.SystemChannel is not null)
        {
            try
            {
                await guild.SystemChannel.SendMessageAsync(embed: embed.Build()).ConfigureAwait(false);
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not post startup report to guild system channel.");
            }
        }

        // Fallback: DM the first configured error user
        if (_options.ErrorNotifyUserIds.Count > 0 &&
            ulong.TryParse(_options.ErrorNotifyUserIds[0], out var userId))
        {
            try
            {
                var user = await _discordClient.GetUserAsync(userId).ConfigureAwait(false);
                if (user is not null)
                {
                    var dm = await user.CreateDMChannelAsync().ConfigureAwait(false);
                    await dm.SendMessageAsync(embed: embed.Build()).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not send startup report via DM.");
            }
        }
    }
}

/// <summary>
/// Mutable validation report built during startup checks.
/// </summary>
public sealed class ValidationReport
{
    private readonly List<ValidationItem> _items = new();

    /// <summary>
    /// The individual validation results.
    /// </summary>
    public IReadOnlyList<ValidationItem> Items => _items;

    /// <summary>
    /// Whether every checked item is valid.
    /// </summary>
    public bool AllValid => _items.All(i => i.IsValid);

    /// <summary>
    /// The number of invalid items.
    /// </summary>
    public int InvalidCount => _items.Count(i => !i.IsValid);

    /// <summary>
    /// Adds a validation item to the report.
    /// </summary>
    public void Add(string name, bool isValid, string helpText)
    {
        _items.Add(new ValidationItem(name, isValid, helpText));
    }
}

/// <summary>
/// A single configuration validation result.
/// </summary>
public sealed record ValidationItem(string Name, bool IsValid, string HelpText);
