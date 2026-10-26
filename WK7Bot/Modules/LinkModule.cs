namespace WK7Bot.Modules;

using Discord;
using Discord.Interactions;
using Microsoft.Extensions.Logging;
using WK7Bot.Services.Interfaces;

/// <summary>
/// Slash commands for linking Discord users to Steam and Battle.net accounts and for registering
/// Steam wishlist URLs. New links are mirrored into the Home Assistant add-on options when
/// <c>link_auto_write_options</c> is enabled.
/// </summary>
public class LinkModule : BotModuleBase
{
    private readonly IAccountLinkService _accountLinkService;
    private readonly ILogger<LinkModule> _logger;

    /// <summary>
    /// Gets the logger used for error reporting.
    /// </summary>
    protected override ILogger Logger => _logger;

    /// <summary>Initializes a new instance of the <see cref="LinkModule"/> class.</summary>
    /// <param name="accountLinkService">The account link service.</param>
    /// <param name="logger">The logger instance.</param>
    public LinkModule(IAccountLinkService accountLinkService, ILogger<LinkModule> logger)
    {
        _accountLinkService = accountLinkService ?? throw new ArgumentNullException(nameof(accountLinkService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>Links a Steam account from a SteamID64, profile URL or vanity URL.</summary>
    /// <param name="steam">The SteamID64 or Steam profile URL.</param>
    [SlashCommand("link-steam", "Verknüpft deinen Discord-Account mit Steam.")]
    public async Task LinkSteamAsync([Summary("steam", "SteamID64 oder Profil-URL")] string steam)
    {
        await DeferAsync(ephemeral: true).ConfigureAwait(false);
        var result = await _accountLinkService.LinkSteamAsync(Context.User.Id, steam);
        await FollowupAsync(result.Success ? $"✅ {result.Message}" : $"❌ {result.Message}", ephemeral: true).ConfigureAwait(false);
    }

    /// <summary>Links a Battle.net account from a refresh token.</summary>
    /// <param name="refreshToken">The Battle.net OAuth refresh token.</param>
    /// <param name="region">The optional region (eu, us, kr, tw).</param>
    /// <param name="battleTag">The optional BattleTag.</param>
    [SlashCommand("link-battlenet", "Verknüpft deinen Discord-Account mit Battle.net.")]
    public async Task LinkBattleNetAsync(
        [Summary("refresh_token", "Battle.net OAuth Refresh-Token")] string refreshToken,
        [Summary("region", "Region: eu, us, kr oder tw")] string? region = null,
        [Summary("battle_tag", "Optionaler BattleTag")] string? battleTag = null)
    {
        await DeferAsync(ephemeral: true).ConfigureAwait(false);
        var result = await _accountLinkService.LinkBattleNetAsync(Context.User.Id, refreshToken, region, battleTag);
        await FollowupAsync(result.Success ? $"✅ {result.Message}" : $"❌ {result.Message}", ephemeral: true).ConfigureAwait(false);
    }

    /// <summary>Shows the current account links for the invoking user.</summary>
    [SlashCommand("link-status", "Zeigt deine verknüpften Steam- und Battle.net-Accounts.")]
    public async Task LinkStatusAsync()
    {
        await DeferAsync(ephemeral: true).ConfigureAwait(false);
        var links = await _accountLinkService.GetLinksAsync(Context.User.Id);
        if (links.Count == 0)
        {
            await FollowupAsync("Du hast noch keine Accounts verknüpft.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        var lines = links.Select(l => $"• **{l.Provider}** — `{l.ExternalId}`" + (string.IsNullOrWhiteSpace(l.WishlistUrl) ? string.Empty : $" ([Wishlist]({l.WishlistUrl}))"));
        await FollowupAsync(string.Join("\n", lines), ephemeral: true).ConfigureAwait(false);
    }

    /// <summary>Removes a provider link for the invoking user.</summary>
    /// <param name="provider">The provider to unlink (steam or battlenet).</param>
    [SlashCommand("link-remove", "Entfernt eine Account-Verknüpfung.")]
    public async Task LinkRemoveAsync([Summary("provider", "steam oder battlenet")] string provider)
    {
        await DeferAsync(ephemeral: true).ConfigureAwait(false);
        var result = await _accountLinkService.UnlinkAsync(Context.User.Id, provider.Trim().ToLowerInvariant());
        await FollowupAsync(result.Success ? $"✅ {result.Message}" : $"❌ {result.Message}", ephemeral: true).ConfigureAwait(false);
    }

    /// <summary>Registers a Steam wishlist URL and links the resolved Steam account.</summary>
    /// <param name="url">The Steam wishlist URL.</param>
    [SlashCommand("wishlist-add", "Verknüpft eine Steam-Wunschliste über ihre URL.")]
    public async Task WishlistAddAsync([Summary("url", "Steam-Wunschlisten-URL")] string url)
    {
        await DeferAsync(ephemeral: true).ConfigureAwait(false);
        var result = await _accountLinkService.LinkWishlistAsync(Context.User.Id, url);
        await FollowupAsync(result.Success ? $"✅ {result.Message}" : $"❌ {result.Message}", ephemeral: true).ConfigureAwait(false);
    }
}
