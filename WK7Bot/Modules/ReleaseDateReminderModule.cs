namespace WK7Bot.Modules;

using Discord;
using Discord.Interactions;
using Microsoft.Extensions.Logging;
using WK7Bot.Core.Entities;
using WK7Bot.Core.Interfaces;
using WK7Bot.Services.Interfaces;

/// <summary>
/// Slash commands for tracking upcoming game releases.
/// </summary>
public class ReleaseDateReminderModule : BotModuleBase
{
    private readonly IReleaseDateReminderRepository _repository;
    private readonly ISteamStoreService _steamStoreService;
    private readonly ILogger<ReleaseDateReminderModule> _logger;

    /// <summary>Gets the logger used for error reporting.</summary>
    protected override ILogger Logger => _logger;

    /// <summary>Initializes a new instance of the <see cref="ReleaseDateReminderModule"/> class.</summary>
    public ReleaseDateReminderModule(
        IReleaseDateReminderRepository repository,
        ISteamStoreService steamStoreService,
        ILogger<ReleaseDateReminderModule> logger)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _steamStoreService = steamStoreService ?? throw new ArgumentNullException(nameof(steamStoreService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>Adds a game to the invoking user's release-date reminders.</summary>
    /// <param name="game">The game name or Steam AppID.</param>
    [SlashCommand("release-remind", "Erinnert dich an den Release eines Spiels.")]
    public async Task ReleaseRemindAsync([Summary("game", "Name oder Steam AppID des Spiels")] string game)
    {
        await DeferAsync(ephemeral: true).ConfigureAwait(false);

        uint appId;
        string title;

        if (uint.TryParse(game, out var parsedAppId))
        {
            appId = parsedAppId;
            var details = await _steamStoreService.GetAppDetailsAsync(appId).ConfigureAwait(false);
            if (details is null)
            {
                await FollowupAsync($"\u274c Konnte keine Store-Details f\u00fcr AppID **{appId}** finden.", ephemeral: true).ConfigureAwait(false);
                return;
            }

            title = details.Name;
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

        var existing = await _repository.GetForUserAsync(Context.User.Id).ConfigureAwait(false);
        if (existing.Any(r => r.AppId == appId))
        {
            await FollowupAsync($"\u274c **{title}** wird bereits \u00fcberwacht.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        var detailsForDate = await _steamStoreService.GetAppDetailsAsync(appId).ConfigureAwait(false);

        await _repository.AddAsync(new ReleaseDateReminderItem
        {
            DiscordUserId = Context.User.Id,
            AppId = appId,
            Name = string.IsNullOrWhiteSpace(title) ? $"AppID {appId}" : title,
            ReleaseDate = detailsForDate?.ReleaseDate,
            IsTba = detailsForDate?.IsTba ?? false
        }, CancellationToken.None).ConfigureAwait(false);

        var dateText = detailsForDate?.IsTba == true
            ? "noch unbekannt (TBA)"
            : detailsForDate?.ReleaseDate?.ToString("dd.MM.yyyy") ?? "unbekannt";

        await FollowupAsync($"\u2705 **{title}** wird \u00fcberwacht. Release: {dateText}", ephemeral: true).ConfigureAwait(false);
    }

    /// <summary>Removes a game from the invoking user's release-date reminders.</summary>
    /// <param name="appid">The Steam application identifier.</param>
    [SlashCommand("release-unremind", "Entfernt eine Release-Erinnerung.")]
    public async Task ReleaseUnremindAsync([Summary("appid", "Steam AppID")] uint appid)
    {
        await DeferAsync(ephemeral: true).ConfigureAwait(false);
        var removed = await _repository.RemoveAsync(Context.User.Id, appid).ConfigureAwait(false);
        await FollowupAsync(removed
            ? $"\u2705 AppID **{appid}** wurde aus den Release-Erinnerungen entfernt."
            : $"\u274c AppID **{appid}** wird nicht \u00fcberwacht.", ephemeral: true).ConfigureAwait(false);
    }

    /// <summary>Lists the invoking user's release-date reminders.</summary>
    [SlashCommand("release-list", "Zeigt deine Release-Erinnerungen.")]
    public async Task ReleaseListAsync()
    {
        await DeferAsync(ephemeral: true).ConfigureAwait(false);
        var reminders = await _repository.GetForUserAsync(Context.User.Id).ConfigureAwait(false);
        if (reminders.Count == 0)
        {
            await FollowupAsync("Du \u00fcberwachst keine Releases. Nutze `/release-remind <Spiel>`.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        var lines = reminders.Select(r =>
        {
            var dateText = r.IsTba ? "TBA" : r.ReleaseDate?.ToString("dd.MM.yyyy") ?? "unbekannt";
            return $"\u2022 **{r.Name}** \u2014 AppID `{r.AppId}` \u2014 Release: {dateText}";
        });

        await FollowupAsync(string.Join("\n", lines), ephemeral: true).ConfigureAwait(false);
    }
}
