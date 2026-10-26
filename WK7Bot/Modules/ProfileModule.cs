namespace WK7Bot.Modules;

using Discord;
using Discord.Interactions;
using Microsoft.Extensions.Logging;
using WK7Bot.Core.Utilities;
using WK7Bot.Services.Interfaces;

/// <summary>
/// Rich profile card command for linked members: Steam playtime and achievements plus the World of
/// Warcraft characters and Diablo heroes of the linked Battle.net account. Rendered as a Components V2
/// card (container with avatar thumbnail and separators) instead of an embed.
/// </summary>
public class ProfileModule : BotModuleBase
{
    private readonly IProfileService _profileService;
    private readonly ILogger<ProfileModule> _logger;

    /// <summary>
    /// Gets the logger used for error reporting.
    /// </summary>
    protected override ILogger Logger => _logger;

    /// <summary>Initializes a new instance of the <see cref="ProfileModule"/> class.</summary>
    /// <param name="profileService">The profile aggregation service.</param>
    /// <param name="logger">The logger instance.</param>
    public ProfileModule(IProfileService profileService, ILogger<ProfileModule> logger)
    {
        _profileService = profileService ?? throw new ArgumentNullException(nameof(profileService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Posts the profile card of the invoking member (or of the mentioned member).
    /// </summary>
    /// <param name="user">The member to show; defaults to the invoking user.</param>
    /// <returns>A task representing the command response operation.</returns>
    [SlashCommand("profile", "Zeigt eine Profilkarte mit Steam- und Battle.net-Daten.")]
    [RequireContext(ContextType.Guild)]
    public async Task ProfileAsync([Summary("user", "Mitglied, dessen Profil gezeigt werden soll")] IUser? user = null)
    {
        await DeferAsync().ConfigureAwait(false);

        var target = user ?? Context.User;
        var result = await _profileService.GetAsync(target.Id, Context.Guild.Id);

        if (result.Steam is null && result.BattleNet is null)
        {
            await FollowupAsync(
                $"Für **{target.Username}** ist kein Spielkonto verknüpft. Nutze `/link-steam` oder `/link-battlenet`.",
                ephemeral: true);
            return;
        }

        await FollowupAsync(
            components: ProfileCardBuilder.Build(result),
            flags: MessageFlags.ComponentsV2);
    }
}
