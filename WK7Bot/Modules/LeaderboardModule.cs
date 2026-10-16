namespace WK7Bot.Modules;

using Discord;
using Discord.Interactions;
using Microsoft.Extensions.Logging;
using WK7Bot.Services.Interfaces;

/// <summary>
/// Slash command that posts Steam and Battle.net leaderboards for the linked guild members.
/// </summary>
public class LeaderboardModule : InteractionModuleBase<SocketInteractionContext>
{
    private readonly ILeaderboardService _leaderboardService;
    private readonly ILogger<LeaderboardModule> _logger;

    /// <summary>Initializes a new instance of the <see cref="LeaderboardModule"/> class.</summary>
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
        await DeferAsync();
        var result = await _leaderboardService.GetAsync(type, Context.Guild.Id, limit);
        if (result.Entries.Count == 0)
        {
            await FollowupAsync("Keine verknüpften Spieler mit Daten gefunden. Nutze `/link-steam` oder `/link-battlenet`.");
            return;
        }

        var embed = new EmbedBuilder()
            .WithTitle(result.Title)
            .WithColor(Color.Gold)
            .WithTimestamp(DateTimeOffset.UtcNow);

        foreach (var entry in result.Entries)
        {
            embed.AddField($"#{entry.Rank} {entry.DisplayName}", $"{entry.Value}" + (string.IsNullOrWhiteSpace(entry.Detail) ? string.Empty : $" · {entry.Detail}"), inline: false);
        }

        if (!string.IsNullOrWhiteSpace(result.Footer))
        {
            embed.WithFooter(result.Footer);
        }

        await FollowupAsync(embed: embed.Build());
    }
}
