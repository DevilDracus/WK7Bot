namespace WK7Bot.Modules;

using Discord;
using Discord.Interactions;
using Microsoft.Extensions.Logging;
using WK7Bot.Core.Utilities;
using WK7Bot.Models;
using WK7Bot.Services.Interfaces;

/// <summary>
/// Provides slash commands for querying Steam wishlist deal information on demand.
/// </summary>
public class DealsModule : InteractionModuleBase<SocketInteractionContext>
{
    private readonly IWishlistDealService _dealService;
    private readonly ISteamService _steamService;
    private readonly IAccountLinkResolver _accountLinkResolver;
    private readonly CommandRateLimiter _rateLimiter;
    private readonly ILogger<DealsModule> _logger;

    private static readonly TimeSpan DealsCooldown = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Initializes a new instance of the <see cref="DealsModule"/> class.
    /// </summary>
    /// <param name="dealService">The wishlist deal service.</param>
    /// <param name="steamService">The Steam service for resolving user mappings.</param>
    /// <param name="accountLinkResolver">The resolver for runtime account links created via /link.</param>
    /// <param name="rateLimiter">The command rate limiter.</param>
    /// <param name="logger">The logger instance.</param>
    public DealsModule(
        IWishlistDealService dealService,
        ISteamService steamService,
        IAccountLinkResolver accountLinkResolver,
        CommandRateLimiter rateLimiter,
        ILogger<DealsModule> logger)
    {
        _dealService = dealService ?? throw new ArgumentNullException(nameof(dealService));
        _steamService = steamService ?? throw new ArgumentNullException(nameof(steamService));
        _accountLinkResolver = accountLinkResolver ?? throw new ArgumentNullException(nameof(accountLinkResolver));
        _rateLimiter = rateLimiter ?? throw new ArgumentNullException(nameof(rateLimiter));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Queries IsThereAnyDeal for current best prices on the invoking user's Steam wishlist.
    /// </summary>
    /// <returns>A task tracking the asynchronous command execution.</returns>
    [SlashCommand("deals", "Shows current best prices for games on your Steam wishlist.")]
    public async Task GetDealsAsync()
    {
        if (!_rateLimiter.CanExecute(Context.User.Id, "deals", DealsCooldown))
        {
            await RespondAsync("⏳ Bitte warte 30 Sekunden, bevor du erneut nach Deals suchst.", ephemeral: true);
            return;
        }

        await DeferAsync(ephemeral: true);

        try
        {
            var discordUserId = Context.User.Id.ToString();
            var steamId = _steamService.GetSteamIdForDiscordUser(discordUserId);
            if (string.IsNullOrWhiteSpace(steamId))
            {
                steamId = await _accountLinkResolver.GetSteamIdAsync(Context.User.Id);
            }

            if (string.IsNullOrWhiteSpace(steamId))
            {
                await FollowupAsync("❌ Du hast keinen Steam-Account mit deinem Discord-Profil verknüpft.", ephemeral: true);
                return;
            }

            var wishlist = await _dealService.FetchWishlistAsync(steamId);
            if (wishlist.Count == 0)
            {
                await FollowupAsync("❌ Deine Steam-Wunschliste ist leer oder privat. Bitte stelle sicher, dass sie öffentlich ist.", ephemeral: true);
                return;
            }

            var prices = await _dealService.FetchPricesAsync(wishlist.Select(w => w.AppId));
            if (prices.Count == 0)
            {
                await FollowupAsync("❌ Keine Preisdaten verfügbar. Bitte versuche es später erneut.", ephemeral: true);
                return;
            }

            var embed = BuildDealsEmbed(wishlist, prices);
            await FollowupAsync(components: CardBuilder.FromEmbed(embed.Build()), flags: MessageFlags.ComponentsV2, ephemeral: true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Error occurred while executing /deals slash command.");
            await FollowupAsync("❌ Bei der Deals-Abfrage ist ein unerwarteter Fehler aufgetreten.", ephemeral: true);
        }
    }

    /// <summary>
    /// Builds a Discord embed listing the current best prices for wishlisted games.
    /// </summary>
    /// <param name="wishlist">The user's wishlist items.</param>
    /// <param name="prices">The price data from IsThereAnyDeal.</param>
    /// <returns>The configured embed builder.</returns>
    private static EmbedBuilder BuildDealsEmbed(IReadOnlyList<SteamWishlistItem> wishlist, IReadOnlyDictionary<uint, ItadPriceData> prices)
    {
        var embed = new EmbedBuilder()
            .WithTitle("🎮 Deine Steam-Wunschlisten-Deals")
            .WithColor(Color.Blue)
            .WithTimestamp(DateTimeOffset.UtcNow)
            .WithFooter("Daten von IsThereAnyDeal");

        foreach (var item in wishlist.Take(25))
        {
            if (!prices.TryGetValue(item.AppId, out var priceData))
            {
                continue;
            }

            var dealText = $"{priceData.CurrentPrice:F2} €";
            if (priceData.DiscountPercent > 0)
            {
                dealText += $" (-{priceData.DiscountPercent}%, regulär {priceData.RegularPrice:F2} €)";
            }
            if (priceData.IsHistoricalLow)
            {
                dealText += " 🏆";
            }

            embed.AddField(item.Name, $"[{dealText}]({priceData.DealUrl}) bei {priceData.StoreName}", inline: false);
        }

        if (embed.Fields.Count == 0)
        {
            embed.WithDescription("Keine aktuellen Deals für deine Wunschliste gefunden.");
        }

        return embed;
    }
}
