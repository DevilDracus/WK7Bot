namespace WK7Bot.Modules;

using System.Globalization;
using Discord;
using Discord.Interactions;
using WK7Bot.Core.Interfaces;
using WK7Bot.Services.Interfaces;

/// <summary>
/// Slash command for showing the stored discount history of a game as a text chart.
/// </summary>
public class PriceHistoryModule : BotModuleBase
{
    private readonly IPriceHistoryRepository _priceHistoryRepository;
    private readonly ISteamStoreService _steamStoreService;
    private readonly IWishlistDealService _dealService;
    private readonly ILogger<PriceHistoryModule> _logger;

    /// <summary>Gets the logger used for error reporting.</summary>
    protected override ILogger Logger => _logger;

    /// <summary>Number of days of price history shown by the command.</summary>
    private const int PriceHistoryWindowDays = 30;

    /// <summary>Initializes a new instance of the <see cref="PriceHistoryModule"/> class.</summary>
    public PriceHistoryModule(
        IPriceHistoryRepository priceHistoryRepository,
        ISteamStoreService steamStoreService,
        IWishlistDealService dealService,
        ILogger<PriceHistoryModule> logger)
    {
        _priceHistoryRepository = priceHistoryRepository ?? throw new ArgumentNullException(nameof(priceHistoryRepository));
        _steamStoreService = steamStoreService ?? throw new ArgumentNullException(nameof(steamStoreService));
        _dealService = dealService ?? throw new ArgumentNullException(nameof(dealService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>Shows the discount history chart for a game.</summary>
    /// <param name="game">The game name or Steam AppID.</param>
    [SlashCommand("price-history", "Zeigt den Preisverlauf eines Spiels als Textdiagramm.")]
    public async Task PriceHistoryAsync([Summary("game", "Name oder Steam AppID des Spiels")] string game)
    {
        await DeferAsync(ephemeral: true).ConfigureAwait(false);
        try
        {

        uint? appId = null;
        string title = game;

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

            (appId, title) = search.Value;
        }

        if (!appId.HasValue)
        {
            await FollowupAsync("\u274c Konnte keine AppID aufl\u00f6sen.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        var since = DateTime.UtcNow.AddDays(-PriceHistoryWindowDays);
        var snapshots = await _priceHistoryRepository.GetSnapshotsAsync(appId.Value, since).ConfigureAwait(false);

        if (snapshots.Count < 2)
        {
            var current = await _dealService.FetchPricesAsync(new[] { appId.Value }).ConfigureAwait(false);
            if (current.TryGetValue(appId.Value, out var data))
            {
                await FollowupAsync(
                    $"**{data.Title}** — aktuell {data.CurrentPrice:C} bei {data.StoreName} ({data.DiscountPercent}% Rabatt).\n" +
                    "Noch nicht genug Verlaufsdaten f\u00fcr ein Diagramm; ich sammle Preise bei jedem Deal-Check.",
                    ephemeral: true).ConfigureAwait(false);
            }
            else
            {
                await FollowupAsync(
                    $"**{title}** — noch keine Verlaufsdaten vorhanden. Der Verlauf wird bei den n\u00e4chsten Deal-Checks aufgebaut.",
                    ephemeral: true).ConfigureAwait(false);
            }

            return;
        }

        var chart = PriceHistoryChartRenderer.Render(snapshots);
        var best = snapshots.OrderBy(s => s.Price).First();
        var latest = snapshots.Last();

        var embed = new EmbedBuilder()
            .WithTitle($"\ud83d\udcc8 Preisverlauf: {latest.Name}")
            .WithDescription($"```\n{chart}\n```")
            .AddField("Aktuell", $"{latest.Price:C} bei {latest.Store} ({latest.DiscountPercent}%)", inline: true)
            .AddField($"Bestpreis ({PriceHistoryWindowDays} Tage)", $"{best.Price:C} bei {best.Store}", inline: true)
            .WithUrl($"https://store.steampowered.com/app/{appId.Value}")
            .WithColor(Color.Purple)
            .WithTimestamp(DateTimeOffset.UtcNow)
            .WithFooter("WK7 Bot • Preisverlauf")
            .Build();

        await FollowupAsync(embed: embed, ephemeral: true).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || ex is TaskCanceledException)
        {
            Logger.LogError(ex, "Error in price-history command.");
            await FollowupAsync("❌ Preisverlauf konnte nicht geladen werden.", ephemeral: true).ConfigureAwait(false);
        }
    }
}

/// <summary>
/// Renders a simple ASCII line chart of discount percentages over time.
/// </summary>
internal static class PriceHistoryChartRenderer
{
    /// <summary>
    /// Renders the discount history as a text chart.
    /// </summary>
    /// <param name="snapshots">The snapshots to render, ordered oldest to newest.</param>
    /// <returns>A multi-line string suitable for a code block.</returns>
    public static string Render(IReadOnlyList<Core.Entities.PriceHistorySnapshot> snapshots)
    {
        if (snapshots.Count == 0)
        {
            return "Keine Daten";
        }

        // Bucket snapshots by day and keep the highest discount of each day.
        var byDay = snapshots
            .GroupBy(s => s.RecordedAt.Date)
            .Select(g => new { Date = g.Key, Discount = g.Max(s => s.DiscountPercent) })
            .OrderBy(x => x.Date)
            .ToList();

        const int width = 40;
        const int height = 10;

        var maxDiscount = byDay.Max(x => x.Discount);
        var minDiscount = byDay.Min(x => x.Discount);
        if (maxDiscount == minDiscount)
        {
            maxDiscount = Math.Max(maxDiscount, 1);
        }

        var rows = new char[height, width];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                rows[y, x] = ' ';
            }
        }

        for (var i = 0; i < byDay.Count; i++)
        {
            var x = (int)Math.Round(i * (width - 1) / (double)Math.Max(byDay.Count - 1, 1));
            var y = height - 1 - (int)Math.Round((byDay[i].Discount - minDiscount) / (double)(maxDiscount - minDiscount) * (height - 1));
            rows[y, x] = '\u2588';
        }

        var lines = new List<string>();
        for (var y = 0; y < height; y++)
        {
            var label = y == 0 ? $"{maxDiscount,3}%" :
                y == height - 1 ? $"{minDiscount,3}%" : "    ";
            var row = new string(Enumerable.Range(0, width).Select(x => rows[y, x]).ToArray());
            lines.Add($"{label} │{row}");
        }

        var firstDate = byDay.First().Date.ToString("dd.MM.", CultureInfo.GetCultureInfo("de-DE"));
        var lastDate = byDay.Last().Date.ToString("dd.MM.", CultureInfo.GetCultureInfo("de-DE"));
        lines.Add($"    └{new string('─', width)}");
        lines.Add($"     {firstDate,-18}{lastDate,22}");

        return string.Join("\n", lines);
    }
}
