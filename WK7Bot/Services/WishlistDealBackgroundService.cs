namespace WK7Bot.Services;

using System.Text;
using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;
using WK7Bot.Core.Entities;
using WK7Bot.Core.Interfaces;
using WK7Bot.Core.Utilities;
using WK7Bot.Models;
using WK7Bot.Options;
using WK7Bot.Services.Interfaces;

/// <summary>
/// Background service that periodically fetches Steam wishlists for mapped users, queries IsThereAnyDeal
/// for current prices, and posts deal alerts to a dedicated Discord channel when thresholds are met.
/// </summary>
public class WishlistDealBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly DiscordSocketClient _discordClient;
    private readonly IWishlistDealService _dealService;
    private readonly ISteamService _steamService;
    private readonly FeatureHealthTracker _health;
    private readonly Wk7BotOptions _options;
    private readonly FeatureGate _featureGate;
    private readonly ILogger<WishlistDealBackgroundService> _logger;

    /// <summary>
    /// Cooldown between alerts for the same game to prevent spam.
    /// </summary>
    private static readonly TimeSpan AlertCooldown = ServiceCooldowns.WishlistDealAlert;

    /// <summary>
    /// Maximum age of alert history entries to retain.
    /// </summary>
    private static readonly TimeSpan AlertRetention = TimeSpan.FromDays(30);

    /// <summary>
    /// Initializes a new instance of the <see cref="WishlistDealBackgroundService"/> class.
    /// </summary>
    /// <param name="scopeFactory">The scope factory for creating database scopes.</param>
    /// <param name="discordClient">The connected Discord socket client.</param>
    /// <param name="dealService">The wishlist deal evaluation service.</param>
    /// <param name="steamService">The Steam service for resolving Discord-to-Steam mappings.</param>
    /// <param name="health">The feature health tracker.</param>
    /// <param name="options">The application configuration options.</param>
    /// <param name="featureGate">The consolidated feature gate.</param>
    /// <param name="logger">The logger instance.</param>
    public WishlistDealBackgroundService(
        IServiceScopeFactory scopeFactory,
        DiscordSocketClient discordClient,
        IWishlistDealService dealService,
        ISteamService steamService,
        FeatureHealthTracker health,
        IOptions<Wk7BotOptions> options,
        FeatureGate featureGate,
        ILogger<WishlistDealBackgroundService> logger)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _discordClient = discordClient ?? throw new ArgumentNullException(nameof(discordClient));
        _dealService = dealService ?? throw new ArgumentNullException(nameof(dealService));
        _steamService = steamService ?? throw new ArgumentNullException(nameof(steamService));
        _health = health ?? throw new ArgumentNullException(nameof(health));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _featureGate = featureGate ?? throw new ArgumentNullException(nameof(featureGate));
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_featureGate.IsEnabled(FeatureKeys.WishlistDealAlerts))
        {
            _logger.LogInformation("Wishlist deal alerts are disabled via feature options.");
            return;
        }

        _logger.LogInformation("Starting Wishlist Deal Background Service.");

        var interval = TimeSpan.FromHours(_options.DealCheckIntervalHours);

        await BackgroundServiceResilience.RunWithResilienceAsync(
            async token =>
            {
                await CheckDealsAsync(token);
            },
            FeatureKeys.WishlistDealAlerts,
            _health,
            _logger,
            interval,
            maxConsecutiveFailures: BackgroundServiceResilience.DefaultMaxConsecutiveFailures,
            restartDelay: BackgroundServiceResilience.DefaultRestartDelay,
            stoppingToken);
    }

    /// <summary>
    /// Fetches wishlists for all mapped users, updates the tracked game set, queries prices,
    /// evaluates alerts, and posts them to the deals channel.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    private async Task CheckDealsAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<SteamAccountLink> mappings;
        using (var scope = _scopeFactory.CreateScope())
        {
            var resolver = scope.ServiceProvider.GetRequiredService<IAccountLinkResolver>();
            mappings = await resolver.GetSteamLinksAsync(cancellationToken);
        }

        if (mappings.Count == 0)
        {
            _logger.LogInformation("No Discord-Steam mappings configured; skipping wishlist deal check.");
            return;
        }

        var (wishlistsByDiscordId, allAppIds) = await FetchWishlistsAsync(mappings, cancellationToken);
        if (allAppIds.Count == 0)
        {
            _logger.LogInformation("No wishlist items found for any mapped user.");
            return;
        }

        _logger.LogInformation("Found {Count} unique wishlisted games across {Users} mapped users.", allAppIds.Count, wishlistsByDiscordId.Count);

        await UpsertTrackedGamesAsync(wishlistsByDiscordId, allAppIds, cancellationToken);

        // Fetch current prices
        var prices = await _dealService.FetchPricesAsync(allAppIds, cancellationToken);
        if (prices.Count == 0)
        {
            _logger.LogWarning("No price data returned from IsThereAnyDeal.");
            return;
        }

        // Evaluate alerts
        var alerts = await EvaluateAlertsAsync(wishlistsByDiscordId, prices, cancellationToken);

        if (alerts.Count == 0)
        {
            _logger.LogInformation("No deal alerts met the threshold criteria.");
            return;
        }

        _logger.LogInformation("{Count} deal alerts triggered; posting to channel.", alerts.Count);

        // Post alerts
        var posted = await PostAlertsAsync(alerts, cancellationToken);
        if (posted)
        {
            await RecordTriggeredAlertsAsync(alerts, cancellationToken);
        }
        else
        {
            _logger.LogWarning("No deal alerts could be delivered to any target guild.");
        }
    }

    /// <summary>
    /// Fetches the wishlists for every effective Discord-Steam mapping (options plus runtime links).
    /// </summary>
    /// <param name="mappings">The merged Discord-Steam mappings.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>A tuple containing the wishlists grouped by Discord user id and the set of all wishlisted application ids.</returns>
    private async Task<(Dictionary<ulong, List<SteamWishlistItem>> WishlistsByDiscordId, HashSet<uint> AllAppIds)> FetchWishlistsAsync(
        IReadOnlyList<SteamAccountLink> mappings,
        CancellationToken cancellationToken)
    {
        var wishlistsByDiscordId = new Dictionary<ulong, List<SteamWishlistItem>>();
        var allAppIds = new HashSet<uint>();

        foreach (var mapping in mappings)
        {
            var discordUserId = mapping.DiscordUserId;
            var wishlist = await _dealService.FetchWishlistAsync(mapping.SteamId, mapping.WishlistUrl, cancellationToken);
            if (wishlist.Count > 0)
            {
                wishlistsByDiscordId[discordUserId] = wishlist.ToList();
                foreach (var item in wishlist)
                {
                    allAppIds.Add(item.AppId);
                }
            }
        }

        return (wishlistsByDiscordId, allAppIds);
    }

    /// <summary>
    /// Replaces the tracked wishlist games in the database and prunes aged alert history.
    /// </summary>
    /// <param name="wishlistsByDiscordId">Wishlists grouped by Discord user id.</param>
    /// <param name="allAppIds">The set of all wishlisted application ids.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>A task tracking the asynchronous operation.</returns>
    private async Task UpsertTrackedGamesAsync(
        Dictionary<ulong, List<SteamWishlistItem>> wishlistsByDiscordId,
        HashSet<uint> allAppIds,
        CancellationToken cancellationToken)
    {
        using (var scope = _scopeFactory.CreateScope())
        {
            var repository = scope.ServiceProvider.GetRequiredService<IWishlistDealRepository>();
            // Look up each game name once instead of rescanning all wishlists for every appId.
            var nameByAppId = wishlistsByDiscordId
                .SelectMany(x => x.Value)
                .GroupBy(item => item.AppId)
                .ToDictionary(group => group.Key, group => group.First().Name);

            var games = allAppIds.Select(appId => new WishlistGame
            {
                AppId = appId,
                Name = nameByAppId.GetValueOrDefault(appId, string.Empty),
                LastUpdated = DateTimeOffset.UtcNow
            }).ToList();

            await repository.ReplaceTrackedGamesAsync(games, cancellationToken);
            await repository.PruneAlertHistoryAsync(AlertRetention, cancellationToken);
        }
    }

    /// <summary>
    /// Evaluates deal alerts for the current prices and records triggered alerts.
    /// </summary>
    /// <param name="wishlistsByDiscordId">Wishlists grouped by Discord user id.</param>
    /// <param name="prices">The current price data keyed by application id.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>The list of triggered alerts.</returns>
    private async Task<List<WishlistDealAlert>> EvaluateAlertsAsync(
        Dictionary<ulong, List<SteamWishlistItem>> wishlistsByDiscordId,
        IReadOnlyDictionary<uint, ItadPriceData> prices,
        CancellationToken cancellationToken)
    {
        // Build reverse mapping: appId -> list of Discord user IDs who wishlisted it
        var usersByAppId = new Dictionary<uint, List<ulong>>();
        foreach (var (discordUserId, items) in wishlistsByDiscordId)
        {
            foreach (var item in items)
            {
                if (!usersByAppId.ContainsKey(item.AppId))
                {
                    usersByAppId[item.AppId] = new List<ulong>();
                }
                usersByAppId[item.AppId].Add(discordUserId);
            }
        }

        var alerts = new List<WishlistDealAlert>();
        using (var scope = _scopeFactory.CreateScope())
        {
            var repository = scope.ServiceProvider.GetRequiredService<IWishlistDealRepository>();

            foreach (var (appId, priceData) in prices)
            {
                if (!usersByAppId.TryGetValue(appId, out var userIds))
                {
                    continue;
                }

                if (await repository.IsOnCooldownAsync(appId, AlertCooldown, cancellationToken))
                {
                    continue;
                }

                var gameTitle = wishlistsByDiscordId
                    .SelectMany(x => x.Value)
                    .FirstOrDefault(g => g.AppId == appId)?.Name ?? priceData.Title;

                var alert = _dealService.EvaluateAlert(appId, gameTitle, priceData, userIds);
                if (alert != null)
                {
                    alerts.Add(alert);
                }
            }
        }

        return alerts;
    }

    /// <summary>
    /// Posts deal alert embeds to the configured deals channel for each target guild.
    /// </summary>
    /// <param name="alerts">The alerts to post.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    private async Task<bool> PostAlertsAsync(IReadOnlyList<WishlistDealAlert> alerts, CancellationToken cancellationToken)
    {
        var targetGuildIds = GetTargetGuildIds();
        var postedAny = false;

        foreach (var guildId in targetGuildIds)
        {
            var channel = await GetOrCreateDealsChannelAsync(guildId);
            if (channel == null)
            {
                _logger.LogWarning("Could not resolve or create deals channel for guild {GuildId}.", guildId);
                continue;
            }

            foreach (var alert in alerts)
            {
                var embed = BuildDealEmbed(alert);
                var mentions = string.Join(" ", alert.NotifiedUserIds.Select(id => $"<@{id}>"));
                await channel.SendMessageAsync(text: mentions, embed: embed, allowedMentions: new AllowedMentions { UserIds = alert.NotifiedUserIds });
                postedAny = true;
            }
        }

        return postedAny;
    }

    /// <summary>
    /// Records cooldown entries only after the alert round was successfully delivered, so a send failure
    /// still leaves the next pass free to retry.
    /// </summary>
    private async Task RecordTriggeredAlertsAsync(IReadOnlyList<WishlistDealAlert> alerts, CancellationToken cancellationToken)
    {
        using (var scope = _scopeFactory.CreateScope())
        {
            var repository = scope.ServiceProvider.GetRequiredService<IWishlistDealRepository>();

            foreach (var alert in alerts)
            {
                await repository.RecordAlertAsync(alert.AppId, alert.IsHistoricalLow, cancellationToken);
            }
        }
    }

    /// <summary>
    /// Builds a Discord embed for a single deal alert.
    /// </summary>
    /// <param name="alert">The deal alert data.</param>
    /// <returns>The constructed embed.</returns>
    private static Embed BuildDealEmbed(WishlistDealAlert alert)
    {
        var embed = new EmbedBuilder()
            .WithTitle($"🎮 {alert.GameTitle}")
            .WithColor(alert.IsHistoricalLow ? Color.Gold : Color.Green)
            .WithTimestamp(DateTimeOffset.UtcNow);

        var priceText = new StringBuilder();
        priceText.AppendLine($"💰 **Aktuell:** {alert.CurrentPrice:F2} €");
        priceText.AppendLine($"📊 **Regulär:** ~~{alert.RegularPrice:F2} €~~");
        priceText.AppendLine($"🔖 **Rabatt:** {alert.DiscountPercent}%");
        priceText.AppendLine($"🏪 **Shop:** {alert.StoreName}");

        if (alert.IsHistoricalLow)
        {
            priceText.AppendLine($"🏆 **Historischer Tiefstpreis!** (Vorher: {alert.HistoricalLow:F2} €)");
        }
        else
        {
            priceText.AppendLine($"📉 **Historischer Tiefstpreis:** {alert.HistoricalLow:F2} €");
        }

        embed.AddField("Preisdetails", priceText.ToString(), inline: false);
        embed.WithUrl(alert.DealUrl);
        embed.WithFooter("Daten von IsThereAnyDeal");

        return embed.Build();
    }

    /// <summary>
    /// Returns the IDs of the guilds that should receive deal alerts.
    /// </summary>
    /// <returns>The target Discord guild IDs.</returns>
    protected virtual IReadOnlyList<ulong> GetTargetGuildIds()
        => AutomaticTargetResolver.Resolve(
            _options.Servers,
            FeatureKeys.RoutedFeatures.WishlistDealAlerts,
            _discordClient.Guilds.Select(g => g.Id),
            _logger);

    /// <summary>
    /// Resolves the deals notification channel for a guild by name, creating it when missing.
    /// </summary>
    /// <param name="guildId">The target guild.</param>
    /// <returns>The text channel instance, or null when the guild cannot be resolved.</returns>
    protected virtual Task<ITextChannel?> GetOrCreateDealsChannelAsync(ulong guildId)
    {
        var guild = _discordClient.GetGuild(guildId);
        return guild is null
            ? Task.FromResult<ITextChannel?>(null)
            : ChannelResolver.TryGetOrCreateFeatureChannelAsync(
                guild,
                _discordClient.CurrentUser.Id,
                _options.DealAlertChannelName,
                "Automatische Steam-Wunschlisten-Deal-Benachrichtigungen.",
                _logger,
                allowReactions: false);
    }
}
