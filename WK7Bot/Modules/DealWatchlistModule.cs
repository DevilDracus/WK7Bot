namespace WK7Bot.Modules;

using Discord;
using Discord.Interactions;
using Microsoft.Extensions.Logging;
using WK7Bot.Services.Interfaces;

/// <summary>
/// Slash commands for the per-user deal watchlist.
/// </summary>
public class DealWatchlistModule : BotModuleBase
{
    private readonly IDealWatchlistService _watchlistService;
    private readonly ILogger<DealWatchlistModule> _logger;

    /// <summary>
    /// Gets the logger used for error reporting.
    /// </summary>
    protected override ILogger Logger => _logger;

    /// <summary>Initializes a new instance of the <see cref="DealWatchlistModule"/> class.</summary>
    public DealWatchlistModule(IDealWatchlistService watchlistService, ILogger<DealWatchlistModule> logger)
    {
        _watchlistService = watchlistService ?? throw new ArgumentNullException(nameof(watchlistService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>Adds a game to the invoking user's watchlist.</summary>
    /// <param name="game">The game name to search for.</param>
    /// <param name="min_discount">Optional minimum discount percentage.</param>
    /// <param name="target_price">Optional absolute target price.</param>
    [SlashCommand("watch", "Beobachtet ein Spiel und benachrichtigt dich bei Preisstürzen.")]
    public async Task WatchAsync(
        [Summary("game", "Name des Spiels")] string game,
        [Summary("min_discount", "Mindestrabatt in Prozent")] int? min_discount = null,
        [Summary("target_price", "Optionaler Zielpreis in Euro")] double? target_price = null)
    {
        await DeferAsync(ephemeral: true);
        var result = await _watchlistService.WatchAsync(Context.User.Id, game, min_discount, target_price.HasValue ? (decimal)target_price.Value : null);
        await FollowupAsync(result.Success ? $"✅ {result.Message}" : $"❌ {result.Message}", ephemeral: true);
    }

    /// <summary>Removes a game from the invoking user's watchlist.</summary>
    /// <param name="game">The game name to remove.</param>
    [SlashCommand("unwatch", "Entfernt ein Spiel von deiner Watchlist.")]
    public async Task UnwatchAsync([Summary("game", "Name des Spiels")] string game)
    {
        await DeferAsync(ephemeral: true);
        var result = await _watchlistService.UnwatchAsync(Context.User.Id, game);
        await FollowupAsync(result.Success ? $"✅ {result.Message}" : $"❌ {result.Message}", ephemeral: true);
    }

    /// <summary>Lists the invoking user's watchlist.</summary>
    [SlashCommand("watchlist", "Zeigt deine beobachteten Spiele.")]
    public async Task WatchlistAsync()
    {
        await DeferAsync(ephemeral: true);
        var items = await _watchlistService.GetWatchlistAsync(Context.User.Id);
        if (items.Count == 0)
        {
            await FollowupAsync("Deine Watchlist ist leer. Nutze `/watch <Spiel>`.", ephemeral: true);
            return;
        }

        var lines = items.Select(i => $"• **{i.GameName}** — ab {i.MinDiscountPercent}%" + (i.TargetPrice.HasValue ? $" oder {i.TargetPrice.Value:F2} €" : string.Empty));
        await FollowupAsync(string.Join("\n", lines), ephemeral: true);
    }
}
