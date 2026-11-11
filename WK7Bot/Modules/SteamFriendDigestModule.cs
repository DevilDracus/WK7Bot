namespace WK7Bot.Modules;

using Discord;
using Discord.Interactions;
using Microsoft.Extensions.Options;
using WK7Bot.Options;
using WK7Bot.Services;
using WK7Bot.Services.Interfaces;

/// <summary>
/// Slash command for manually triggering the Steam friend activity digest.
/// </summary>
public class SteamFriendDigestModule : BotModuleBase
{
    private readonly SteamFriendDigestBackgroundService _backgroundService;
    private readonly ISteamService _steamService;
    private readonly Wk7BotOptions _options;
    private readonly ILogger<SteamFriendDigestModule> _logger;

    /// <summary>Gets the logger used for error reporting.</summary>
    protected override ILogger Logger => _logger;

    /// <summary>Initializes a new instance of the <see cref="SteamFriendDigestModule"/> class.</summary>
    public SteamFriendDigestModule(
        SteamFriendDigestBackgroundService backgroundService,
        ISteamService steamService,
        IOptions<Wk7BotOptions> options,
        ILogger<SteamFriendDigestModule> logger)
    {
        _backgroundService = backgroundService ?? throw new ArgumentNullException(nameof(backgroundService));
        _steamService = steamService ?? throw new ArgumentNullException(nameof(steamService));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>Manually triggers the Steam friend activity digest.</summary>
    [SlashCommand("steam-friends-digest", "Postet den Steam-Freunde Aktivit\u00e4ts-\u00dcberblick sofort.")]
    public async Task TriggerAsync()
    {
        await DeferAsync(ephemeral: true).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(_options.SteamApiKey))
        {
            await FollowupAsync("\u274c Kein Steam-API-Key konfiguriert.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        var steamIds = _options.DiscordSteamMappings
            .Select(m => m.SteamId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToList();

        if (steamIds.Count == 0)
        {
            await FollowupAsync("\u274c Keine Discord-Steam-Verkn\u00fcpfungen konfiguriert.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        try
        {
            await _backgroundService.BuildAndPostDigestAsync(steamIds, CancellationToken.None).ConfigureAwait(false);
            await FollowupAsync("\u2705 Steam-Freunde Digest wurde ausgel\u00f6st.", ephemeral: true).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Manual Steam friend digest failed.");
            await FollowupAsync("\u274c Der Digest konnte nicht ausgel\u00f6st werden.", ephemeral: true).ConfigureAwait(false);
        }
    }
}
