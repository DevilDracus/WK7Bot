namespace WK7Bot.Core.Utilities;

using Microsoft.Extensions.Options;
using WK7Bot.Options;

/// <summary>
/// Single source of truth for feature flag decisions. Used by both the DI registration layer
/// (to decide whether to register a background service) and by runtime services (to decide
/// whether to execute). This eliminates the previous split where <c>IsFeatureEnabled</c> in
/// <c>ServiceCollectionExtensions</c> and <c>FeatureOptions.IsEnabled</c> were two independent
/// decision points that could diverge.
/// </summary>
public sealed class FeatureGate
{
    private readonly Wk7BotOptions _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="FeatureGate"/> class.
    /// </summary>
    /// <param name="options">The strongly-typed bot options.</param>
    public FeatureGate(IOptions<Wk7BotOptions> options)
    {
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    }

    /// <summary>
    /// Determines whether a feature is enabled. Unknown keys default to enabled so that
    /// adding a new feature flag does not silently disable it on older configurations.
    /// </summary>
    /// <param name="featureKey">The snake_case feature key (e.g. <c>food_service_enabled</c>).</param>
    /// <returns><see langword="true"/> when the feature is enabled.</returns>
    public bool IsEnabled(string featureKey) => featureKey?.ToLowerInvariant() switch
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
        _ => true
    };
}
