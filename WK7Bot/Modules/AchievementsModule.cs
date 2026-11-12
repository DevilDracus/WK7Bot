namespace WK7Bot.Modules;

using Discord;
using Discord.Interactions;
using Microsoft.Extensions.Logging;
using WK7Bot.Services.Interfaces;

/// <summary>
/// Slash commands for Steam achievement tracking.
/// </summary>
public class AchievementsModule : BotModuleBase
{
    /// <summary>Maximum number of unlocked achievements rendered.</summary>
    private const int UnlockedListLimit = 10;

    /// <summary>Maximum number of locked achievements rendered.</summary>
    private const int LockedListLimit = 5;

    /// <summary>Discord's per-field character limit.</summary>
    private const int FieldLimit = 1024;

    /// <summary>Length of the ellipsis suffix appended when a field value is truncated.</summary>
    private const int EllipsisLength = 3;

    /// <summary>Timeout applied to the Steam store search HTTP request.</summary>
    private static readonly TimeSpan StoreSearchTimeout = TimeSpan.FromSeconds(15);

    private readonly ISteamService _steamService;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<AchievementsModule> _logger;

    /// <summary>Gets the logger used for error reporting.</summary>
    protected override ILogger Logger => _logger;

    /// <summary>Initializes a new instance of the <see cref="AchievementsModule"/> class.</summary>
    public AchievementsModule(
        ISteamService steamService,
        IHttpClientFactory httpClientFactory,
        ILogger<AchievementsModule> logger)
    {
        _steamService = steamService ?? throw new ArgumentNullException(nameof(steamService));
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>Shows achievement progress for a game.</summary>
    [SlashCommand("achievements", "Zeigt deinen Achievement-Fortschritt für ein Spiel.")]
    public async Task AchievementsAsync(
        [Summary("game", "Name oder AppID des Spiels")] string game)
    {
        await DeferAsync(ephemeral: true).ConfigureAwait(false);

        var steamId = _steamService.GetSteamIdForDiscordUser(Context.User.Id.ToString());
        if (string.IsNullOrWhiteSpace(steamId))
        {
            await FollowupAsync("❌ Du hast keinen Steam-Account verknüpft. Nutze `/link-steam`.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        uint appId;
        string title = game;

        if (uint.TryParse(game, out var parsedAppId))
        {
            appId = parsedAppId;
        }
        else
        {
            using var http = _httpClientFactory.CreateClient();
            http.Timeout = StoreSearchTimeout;
            try
            {
                var url = $"https://store.steampowered.com/api/storesearch/?term={Uri.EscapeDataString(game)}&cc=de&l=german";
                var response = await http.GetAsync(url, CancellationToken.None).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    await FollowupAsync($"❌ Spiel \"{game}\" nicht gefunden.", ephemeral: true).ConfigureAwait(false);
                    return;
                }
                using var doc = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
                if (!doc.RootElement.TryGetProperty("items", out var items) || items.GetArrayLength() == 0)
                {
                    await FollowupAsync($"❌ Spiel \"{game}\" nicht gefunden.", ephemeral: true).ConfigureAwait(false);
                    return;
                }
                var first = items[0];
                appId = first.GetProperty("id").GetUInt32();
                title = first.GetProperty("name").GetString() ?? game;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Steam store search failed for {Game}.", game);
                await FollowupAsync($"❌ Spiel \"{game}\" nicht gefunden.", ephemeral: true).ConfigureAwait(false);
                return;
            }
        }

        var achievements = await _steamService.GetEnrichedAchievementsAsync(steamId, appId, CancellationToken.None).ConfigureAwait(false);
        if (achievements.Count == 0)
        {
            await FollowupAsync($"❌ Keine Achievements für **{title}** gefunden (oder das Spiel hat keine Achievements).", ephemeral: true).ConfigureAwait(false);
            return;
        }

        var unlocked = achievements.Count(a => a.UnlockTime.HasValue);
        var total = achievements.Count;
        var percent = total > 0 ? (double)unlocked / total * 100 : 0;

        var embed = new EmbedBuilder()
            .WithTitle($"🏆 {title} — Achievements")
            .WithDescription($"**{unlocked}/{total}** freigeschaltet ({percent:F1}%)")
            .WithColor(Color.Gold);

        // Show unlocked achievements first (most recent), then locked
        var unlockedList = achievements.Where(a => a.UnlockTime.HasValue).OrderByDescending(a => a.UnlockTime).Take(UnlockedListLimit).ToList();
        var lockedList = achievements.Where(a => !a.UnlockTime.HasValue).Take(LockedListLimit).ToList();

        if (unlockedList.Count > 0)
        {
            var text = string.Join("\n", unlockedList.Select(a => $"✅ {a.Name}"));
            embed.AddField("Freigeschaltet (zuletzt)", text.Length > FieldLimit ? text[..(FieldLimit - EllipsisLength)] + "..." : text, inline: false);
        }

        if (lockedList.Count > 0)
        {
            var text = string.Join("\n", lockedList.Select(a => $"🔒 {a.Name}" + (string.IsNullOrWhiteSpace(a.Description) ? "" : $" — {a.Description}")));
            embed.AddField("Noch offen", text.Length > FieldLimit ? text[..(FieldLimit - EllipsisLength)] + "..." : text, inline: false);
        }

        await FollowupAsync(embed: embed.Build(), ephemeral: true).ConfigureAwait(false);
    }
}
