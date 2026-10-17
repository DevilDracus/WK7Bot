namespace WK7Bot.Modules;

using Discord;
using Discord.Interactions;
using Microsoft.Extensions.Logging;
using WK7Bot.Core.Utilities;
using WK7Bot.Options;
using WK7Bot.Services;

/// <summary>
/// Admin commands for inspecting and toggling the bot's feature switches at runtime. A toggle is
/// applied to the in-process <see cref="FeatureGate"/> immediately and persisted to the add-on
/// options through the Supervisor API so it survives a restart.
/// </summary>
[Group("config", "Bot-Konfiguration zur Laufzeit verwalten")]
public class ConfigModule : InteractionModuleBase<SocketInteractionContext>
{
    private readonly FeatureGate _featureGate;
    private readonly AddOnOptionsWriter _optionsWriter;
    private readonly ILogger<ConfigModule> _logger;

    /// <summary>Initializes a new instance of the <see cref="ConfigModule"/> class.</summary>
    /// <param name="featureGate">The consolidated feature gate holding runtime overrides.</param>
    /// <param name="optionsWriter">The Supervisor-backed add-on options writer.</param>
    /// <param name="logger">The logger instance.</param>
    public ConfigModule(FeatureGate featureGate, AddOnOptionsWriter optionsWriter, ILogger<ConfigModule> logger)
    {
        _featureGate = featureGate ?? throw new ArgumentNullException(nameof(featureGate));
        _optionsWriter = optionsWriter ?? throw new ArgumentNullException(nameof(optionsWriter));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Enables or disables one feature switch at runtime and persists it to the add-on options.
    /// </summary>
    /// <param name="key">The snake_case feature key (e.g. <c>food_service_enabled</c>).</param>
    /// <param name="state">Either <c>on</c> or <c>off</c>.</param>
    /// <returns>A task representing the command response operation.</returns>
    [SlashCommand("feature", "Aktiviert oder deaktiviert einen Feature-Schalter (Admin)")]
    [RequireUserPermission(GuildPermission.Administrator)]
    public async Task FeatureAsync(
        [Summary("key", "Feature-Schlüssel, z. B. food_service_enabled")] string key,
        [Summary("state", "on oder off")]
        [Choice("An", "on")]
        [Choice("Aus", "off")]
        string state)
    {
        await DeferAsync(ephemeral: true);

        key = (key ?? string.Empty).Trim().ToLowerInvariant();
        if (!FeatureGate.IsKnownKey(key))
        {
            await FollowupAsync(
                $"❌ Unbekannter Feature-Schlüssel `{key}`. Nutze `/config list` für alle gültigen Schlüssel.",
                ephemeral: true);
            return;
        }

        var enabled = string.Equals(state, "on", StringComparison.OrdinalIgnoreCase)
                      || string.Equals(state, "true", StringComparison.OrdinalIgnoreCase);

        _featureGate.SetOverride(key, enabled);
        var persisted = await _optionsWriter.MergeFeatureAsync(key, enabled);

        var stateText = enabled ? "aktiviert" : "deaktiviert";
        var persistenceText = persisted
            ? "In der Add-on-Konfiguration gespeichert."
            : "⚠️ Konnte nicht in der Add-on-Konfiguration gespeichert werden (Supervisor nicht erreichbar) — die Änderung gilt nur bis zum Neustart.";

        await FollowupAsync(
            $"✅ Feature `{key}` {stateText}. {persistenceText}\n\n" +
            "Laufzeitprüfungen greifen sofort. Hintergrunddienste, die beim Start registriert werden, " +
            "starten bzw. stoppen erst nach einem Add-on-Neustart.",
            ephemeral: true);
    }

    /// <summary>
    /// Lists every known feature switch with its current state and whether a runtime override is active.
    /// </summary>
    /// <returns>A task representing the command response operation.</returns>
    [SlashCommand("list", "Zeigt alle Feature-Schalter und ihren Zustand (Admin)")]
    [RequireUserPermission(GuildPermission.Administrator)]
    public async Task ListAsync()
    {
        await DeferAsync(ephemeral: true);

        var overrides = _featureGate.GetOverrides();
        var lines = FeatureKeys.All
            .OrderBy(key => key, StringComparer.OrdinalIgnoreCase)
            .Select(key =>
            {
                var marker = _featureGate.IsEnabled(key) ? "🟢" : "🔴";
                var suffix = overrides.ContainsKey(key) ? " *(Laufzeit-Override)*" : string.Empty;
                return $"{marker} `{key}`{suffix}";
            });

        await FollowupAsync("**Feature-Schalter**\n" + string.Join("\n", lines), ephemeral: true);
    }
}
