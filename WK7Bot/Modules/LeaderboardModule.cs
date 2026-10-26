namespace WK7Bot.Modules;

using Discord;
using Discord.Interactions;
using Microsoft.Extensions.Logging;
using WK7Bot.Core.Utilities;
using WK7Bot.Services.Interfaces;

/// <summary>
/// Slash command that posts Steam and Battle.net leaderboards for the linked guild members as a
/// Components V2 card.
/// </summary>
public class LeaderboardModule : BotModuleBase
{
    private readonly ILeaderboardService _leaderboardService;
    private readonly ILogger<LeaderboardModule> _logger;

    /// <summary>
    /// Gets the logger used for error reporting.
    /// </summary>
    protected override ILogger Logger => _logger;

    /// <summary>Initializes a new instance of the <see cref="LeaderboardModule"/> class.</summary>
    /// <param name="leaderboardService">The leaderboard aggregation service.</param>
    /// <param name="logger">The logger instance.</param>
    public LeaderboardModule(ILeaderboardService leaderboardService, ILogger<LeaderboardModule> logger)
    {
        _leaderboardService = leaderboardService ?? throw new ArgumentNullException(nameof(leaderboardService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>Posts the requested leaderboard.</summary>
    /// <param name="type">The leaderboard type.</param>
    /// <param name="limit">Maximum number of entries.</param>
    [SlashCommand("leaderboard", "Zeigt eine Bestenliste der verknüpften Spieler.")]
    public async Task LeaderboardAsync(
        [Summary("type", "steam-playtime, steam-achievements, battlenet-wow oder battlenet-diablo")]
        [Choice("Steam Spielzeit", "steam-playtime")]
        [Choice("Steam Errungenschaften", "steam-achievements")]
        [Choice("WoW Stufe", "battlenet-wow")]
        [Choice("Diablo Paragon", "battlenet-diablo")]
        string type = "steam-playtime",
        [Summary("limit", "Anzahl der Einträge (1-25)")] int limit = 10)
    {
        await DeferAsync().ConfigureAwait(false);
        var result = await _leaderboardService.GetAsync(type, Context.Guild.Id, limit);
        if (result.Entries.Count == 0)
        {
            await FollowupAsync("Keine verknüpften Spieler mit Daten gefunden. Nutze `/link-steam` oder `/link-battlenet`.").ConfigureAwait(false);
            return;
        }

        await FollowupAsync(
            components: LeaderboardCardBuilder.Build(result),
            flags: MessageFlags.ComponentsV2);
    }
}
