namespace WK7Bot.Core.Utilities;

using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using WK7Bot.Options;

/// <summary>
/// Single source of truth for feature flag decisions. Used by both the DI registration layer
/// (to decide whether to register a background service) and by runtime services (to decide
/// whether to execute). This eliminates the previous split where <c>IsFeatureEnabled</c> in
/// <c>ServiceCollectionExtensions</c> and <c>FeatureOptions.IsEnabled</c> were two independent
/// decision points that could diverge.
/// Runtime overrides set through <c>/config feature</c> take precedence over the bound options so a
/// toggle takes effect immediately without an add-on restart.
/// </summary>
public sealed class FeatureGate
{
    private readonly Wk7BotOptions _options;
    private readonly ConcurrentDictionary<string, bool> _overrides = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Initializes a new instance of the <see cref="FeatureGate"/> class.
    /// </summary>
    /// <param name="options">The strongly-typed bot options.</param>
    public FeatureGate(IOptions<Wk7BotOptions> options)
    {
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    }

    /// <summary>
    /// Determines whether a feature is enabled. A runtime override wins over the bound option; without
    /// an override the configured flag is used. Unknown keys default to enabled so that adding a new
    /// feature flag does not silently disable it on older configurations.
    /// </summary>
    /// <param name="featureKey">The snake_case feature key (e.g. <c>food_service_enabled</c>).</param>
    /// <returns><see langword="true"/> when the feature is enabled.</returns>
    public bool IsEnabled(string featureKey)
    {
        if (string.IsNullOrWhiteSpace(featureKey))
        {
            return true;
        }

        var key = featureKey.ToLowerInvariant();
        return _overrides.TryGetValue(key, out var overridden) ? overridden : ResolveFromOptions(key);
    }

    /// <summary>
    /// Sets a process-lifetime override for a feature key so runtime checks observe the new state
    /// immediately. The caller is responsible for persisting the change to the add-on options.
    /// </summary>
    /// <param name="featureKey">The snake_case feature key.</param>
    /// <param name="enabled">The new state.</param>
    public void SetOverride(string featureKey, bool enabled)
    {
        if (!string.IsNullOrWhiteSpace(featureKey))
        {
            _overrides[featureKey.ToLowerInvariant()] = enabled;
        }
    }

    /// <summary>Removes a runtime override so the bound option applies again.</summary>
    /// <param name="featureKey">The snake_case feature key.</param>
    /// <returns><see langword="true"/> when an override was removed.</returns>
    public bool ClearOverride(string featureKey)
        => !string.IsNullOrWhiteSpace(featureKey) && _overrides.TryRemove(featureKey.ToLowerInvariant(), out _);

    /// <summary>Gets a snapshot of the active runtime overrides.</summary>
    /// <returns>The overrides keyed by feature key.</returns>
    public IReadOnlyDictionary<string, bool> GetOverrides()
        => new Dictionary<string, bool>(_overrides, StringComparer.OrdinalIgnoreCase);

    /// <summary>Determines whether the supplied key is a known feature key.</summary>
    /// <param name="featureKey">The candidate key.</param>
    /// <returns><see langword="true"/> when the key is one of <see cref="FeatureKeys.All"/>.</returns>
    public static bool IsKnownKey(string? featureKey)
        => !string.IsNullOrWhiteSpace(featureKey)
           && FeatureKeys.All.Contains(featureKey.Trim().ToLowerInvariant(), StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Resolves a feature key against the bound options. Unknown keys default to enabled.
    /// </summary>
    /// <param name="featureKey">The normalized (lowercase) feature key.</param>
    /// <returns><see langword="true"/> when the configured feature is enabled.</returns>
    private bool ResolveFromOptions(string featureKey) => featureKey switch
    {
        FeatureKeys.RssPolling => _options.Features.RssPollingEnabled,
        FeatureKeys.HomeAssistantNotifier => _options.Features.HomeAssistantNotifierEnabled,
        FeatureKeys.LeipzigWaste => _options.Features.LeipzigWasteEnabled,
        FeatureKeys.AlexaNotifications => _options.Features.AlexaNotificationsEnabled,
        FeatureKeys.DiscordPresenceMqtt => _options.Features.DiscordPresenceMqttEnabled,
        FeatureKeys.SteamPresence => _options.Features.SteamPresenceEnabled,
        FeatureKeys.BattleNetPresence => _options.Features.BattleNetPresenceEnabled,
        FeatureKeys.FoodService => _options.Features.FoodServiceEnabled,
        FeatureKeys.SpontanTreff => _options.Features.SpontanTreffEnabled,
        FeatureKeys.WeekendDigest => _options.Features.WeekendDigestEnabled,
        FeatureKeys.DwdWarning => _options.Features.DwdWarningEnabled,
        FeatureKeys.ErrorNotifications => _options.Features.ErrorNotificationsEnabled,
        FeatureKeys.MqttBotStatus => _options.Features.MqttBotStatusEnabled,
        FeatureKeys.WishlistDealAlerts => _options.Features.WishlistDealAlertsEnabled,
        FeatureKeys.Leaderboard => _options.Features.LeaderboardEnabled,
        FeatureKeys.DealWatchlist => _options.Features.DealWatchlistEnabled,
        FeatureKeys.DiscordEvents => _options.Features.DiscordEventsEnabled,
        FeatureKeys.PollenAlerts => _options.Features.PollenAlertsEnabled,
        FeatureKeys.Reminders => _options.Features.RemindersEnabled,
        _ => true
    };
}
