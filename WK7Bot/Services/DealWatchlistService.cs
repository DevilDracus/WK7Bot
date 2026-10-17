namespace WK7Bot.Services;

using System.Globalization;
using System.Text.Json;
using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Options;
using WK7Bot.Core.Entities;
using WK7Bot.Core.Interfaces;
using WK7Bot.Options;
using WK7Bot.Services.Interfaces;
using WK7Bot.Core.Utilities;

/// <summary>
/// Stores per-user deal watchlist entries, resolves game names through the Steam store search API,
/// and sends DM alerts when a watched game reaches the configured discount or target price.
/// </summary>
public class DealWatchlistService : IDealWatchlistService
{
    private readonly IDealWatchRepository _repository;
    private readonly IWishlistDealService _dealService;
    private readonly DiscordSocketClient _discordClient;
    private readonly IOptions<Wk7BotOptions> _options;
    private readonly HttpClient _httpClient;
    private readonly ILogger<DealWatchlistService> _logger;

    /// <summary>Initializes a new instance of the <see cref="DealWatchlistService"/> class.</summary>
    public DealWatchlistService(
        IDealWatchRepository repository,
        IWishlistDealService dealService,
        DiscordSocketClient discordClient,
        IOptions<Wk7BotOptions> options,
        HttpClient httpClient,
        ILogger<DealWatchlistService> logger)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _dealService = dealService ?? throw new ArgumentNullException(nameof(dealService));
        _discordClient = discordClient ?? throw new ArgumentNullException(nameof(discordClient));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<DealWatchResult> WatchAsync(ulong discordUserId, string gameName, int? minDiscountPercent = null, decimal? targetPrice = null, CancellationToken cancellationToken = default)
    {
        var resolved = await ResolveGameAsync(gameName, cancellationToken).ConfigureAwait(false);
        if (resolved is null)
        {
            return new DealWatchResult(false, $"Kein Spiel zu \"{gameName}\" gefunden.");
        }

        var (appId, title) = resolved.Value;
        var existing = await _repository.GetForUserAsync(discordUserId, cancellationToken).ConfigureAwait(false);
        if (existing.Any(w => w.ItadId == appId.ToString(CultureInfo.InvariantCulture)))
        {
            return new DealWatchResult(false, $"{title} steht bereits auf deiner Watchlist.");
        }

        await _repository.AddAsync(new DealWatchItem
        {
            DiscordUserId = discordUserId,
            GameName = title,
            ItadId = appId.ToString(CultureInfo.InvariantCulture),
            MinDiscountPercent = minDiscountPercent ?? _options.Value.DealWatchlistMinDiscountPercent,
            TargetPrice = targetPrice,
            CreatedAt = DateTime.UtcNow
        }, cancellationToken).ConfigureAwait(false);

        return new DealWatchResult(true, $"{title} wird jetzt beobachtet.");
    }

    /// <inheritdoc />
    public async Task<DealWatchResult> UnwatchAsync(ulong discordUserId, string gameName, CancellationToken cancellationToken = default)
    {
        var removed = await _repository.RemoveAsync(discordUserId, gameName, cancellationToken).ConfigureAwait(false);
        return removed
            ? new DealWatchResult(true, $"{gameName} wurde von deiner Watchlist entfernt.")
            : new DealWatchResult(false, $"{gameName} steht nicht auf deiner Watchlist.");
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<DealWatchItem>> GetWatchlistAsync(ulong discordUserId, CancellationToken cancellationToken = default)
        => _repository.GetForUserAsync(discordUserId, cancellationToken);

    /// <inheritdoc />
    public async Task<int> CheckAllAsync(CancellationToken cancellationToken = default)
    {
        var items = await _repository.GetAllAsync(cancellationToken).ConfigureAwait(false);
        if (items.Count == 0)
        {
            return 0;
        }

        var appIds = items
            .Select(i => uint.TryParse(i.ItadId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) ? id : 0u)
            .Where(id => id != 0)
            .Distinct()
            .ToList();
        if (appIds.Count == 0)
        {
            return 0;
        }

        var prices = await _dealService.FetchPricesAsync(appIds, cancellationToken).ConfigureAwait(false);
        var alerts = 0;
        foreach (var item in items)
        {
            if (!uint.TryParse(item.ItadId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var appId) ||
                !prices.TryGetValue(appId, out var price))
            {
                continue;
            }

            var meetsDiscount = price.DiscountPercent >= item.MinDiscountPercent;
            var meetsTarget = item.TargetPrice.HasValue && price.CurrentPrice <= item.TargetPrice.Value;
            if (!meetsDiscount && !meetsTarget)
            {
                await _repository.UpdatePriceAsync(item.Id, price.CurrentPrice, price.DiscountPercent, item.LastAlertedAt, cancellationToken).ConfigureAwait(false);
                continue;
            }

            if (item.LastAlertedAt.HasValue && DateTime.UtcNow - item.LastAlertedAt.Value < TimeSpan.FromDays(3))
            {
                continue;
            }

            var user = _discordClient.GetUser(item.DiscordUserId);
            if (user is null)
            {
                continue;
            }

            try
            {
                var dm = await user.CreateDMChannelAsync().ConfigureAwait(false);
                var embed = new EmbedBuilder()
                    .WithTitle($"🎮 Deal-Alarm: {item.GameName}")
                    .WithDescription($"{price.CurrentPrice:F2} € (-{price.DiscountPercent}%) bei {price.StoreName}")
                    .WithUrl(price.DealUrl)
                    .WithColor(price.IsHistoricalLow ? Color.Gold : Color.Green)
                    .WithTimestamp(DateTimeOffset.UtcNow)
                    .WithFooter("WK7 Bot • Deal-Watchlist");
                await CardSender.SendCardAsync(dm, CardBuilder.FromEmbed(embed.Build()), embed.Build(), _logger, cancellationToken).ConfigureAwait(false);
                await _repository.UpdatePriceAsync(item.Id, price.CurrentPrice, price.DiscountPercent, DateTime.UtcNow, cancellationToken).ConfigureAwait(false);
                alerts++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning(ex, "Could not send deal watch alert to user {UserId}.", item.DiscordUserId);
            }
        }

        return alerts;
    }

    private async Task<(uint AppId, string Title)?> ResolveGameAsync(string gameName, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(gameName))
        {
            return null;
        }

        var url = $"https://store.steampowered.com/api/storesearch/?term={Uri.EscapeDataString(gameName)}&cc=de&l=german";
        try
        {
            using var response = await _httpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
            if (!document.RootElement.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var first = items.EnumerateArray().FirstOrDefault();
            if (first.ValueKind != JsonValueKind.Object || !first.TryGetProperty("id", out var id) || !first.TryGetProperty("name", out var name))
            {
                return null;
            }

            return (id.GetUInt32(), name.GetString() ?? gameName);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Steam store search failed for {Game}.", gameName);
            return null;
        }
    }
}
