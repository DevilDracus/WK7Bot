namespace WK7Bot.Modules;

using Discord;
using Discord.Interactions;
using WK7Bot.Services.Interfaces;

/// <summary>
/// Slash command for comparing current game prices across multiple stores.
/// </summary>
public class PriceComparisonModule : BotModuleBase
{
    /// <summary>Maximum number of store offers rendered in the price comparison embed.</summary>
    private const int OfferLimit = 10;

    private readonly IWishlistDealService _dealService;
    private readonly ISteamStoreService _steamStoreService;
    private readonly ILogger<PriceComparisonModule> _logger;

    /// <summary>Gets the logger used for error reporting.</summary>
    protected override ILogger Logger => _logger;

    /// <summary>Initializes a new instance of the <see cref="PriceComparisonModule"/> class.</summary>
    public PriceComparisonModule(
        IWishlistDealService dealService,
        ISteamStoreService steamStoreService,
        ILogger<PriceComparisonModule> logger)
    {
        _dealService = dealService ?? throw new ArgumentNullException(nameof(dealService));
        _steamStoreService = steamStoreService ?? throw new ArgumentNullException(nameof(steamStoreService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>Compares current prices for a game across stores.</summary>
    /// <param name="game">The game name or Steam AppID.</param>
    [SlashCommand("compare-prices", "Vergleicht aktuelle Preise eines Spiels \u00fcber mehrere Stores.")]
    public async Task ComparePricesAsync([Summary("game", "Name oder Steam AppID des Spiels")] string game)
    {
        await DeferAsync(ephemeral: true).ConfigureAwait(false);
        try
        {

        uint? appId = null;
        if (uint.TryParse(game, out var parsedAppId))
        {
            appId = parsedAppId;
        }
        else
        {
            var search = await _steamStoreService.SearchGameAsync(game).ConfigureAwait(false);
            if (search is null)
            {
                await FollowupAsync($"\u274c Kein Spiel zu \"{game}\" gefunden.", ephemeral: true).ConfigureAwait(false);
                return;
            }

            appId = search.Value.AppId;
        }

        if (!appId.HasValue)
        {
            await FollowupAsync("\u274c Konnte keine AppID aufl\u00f6sen.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        var comparison = await _dealService.ComparePricesAsync(appId.Value).ConfigureAwait(false);
        if (comparison is null || comparison.Offers.Count == 0)
        {
            await FollowupAsync("\u274c Keine Preisdaten gefunden. Ist der ITAD-API-Key konfiguriert?", ephemeral: true).ConfigureAwait(false);
            return;
        }

        var lines = comparison.Offers.Select((o, i) =>
            $"{i + 1}. **{o.StoreName}** \u2014 {o.Price:C} " +
            $"(regul\u00e4r {o.RegularPrice:C}, {o.DiscountPercent}% Rabatt)");

        var embed = new EmbedBuilder()
            .WithTitle($"\ud83d\udd0d Preisvergleich: {comparison.Title}")
            .WithDescription(string.Join("\n", lines.Take(OfferLimit)))
            .WithUrl($"https://store.steampowered.com/app/{appId.Value}")
            .WithColor(Color.Green)
            .WithTimestamp(DateTimeOffset.UtcNow)
            .WithFooter("WK7 Bot • Daten von IsThereAnyDeal")
            .Build();

        await FollowupAsync(embed: embed, ephemeral: true).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || ex is TaskCanceledException)
        {
            Logger.LogError(ex, "Error in compare-prices command.");
            await FollowupAsync("❌ Preisvergleich fehlgeschlagen.", ephemeral: true).ConfigureAwait(false);
        }
    }
}
