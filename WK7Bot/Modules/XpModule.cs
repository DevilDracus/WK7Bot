namespace WK7Bot.Modules;

using Discord;
using Discord.Interactions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WK7Bot.Core.Entities;
using WK7Bot.Core.Interfaces;
using WK7Bot.Core.Utilities;
using WK7Bot.Options;

/// <summary>
/// Leveling &amp; XP commands: <c>/xp leaderboard</c>, <c>/xp reset</c>, <c>/xp set</c>.
/// </summary>
[Group("xp", "Leveling & XP")]
public class XpModule : BotModuleBase
{
    private readonly IXpRepository _repository;
    private readonly IOptions<Wk7BotOptions> _options;
    private readonly ILogger<XpModule> _logger;

    /// <inheritdoc />
    protected override ILogger Logger => _logger;

    /// <summary>Initializes a new instance.</summary>
    public XpModule(IXpRepository repository, IOptions<Wk7BotOptions> options, ILogger<XpModule> logger)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>Shows the caller's XP and level.</summary>
    [SlashCommand("xp", "Zeigt dein Level und XP")]
    public async Task XpAsync()
    {
        await RankAsync().ConfigureAwait(false);
    }

    /// <summary>Shows the caller's XP and level.</summary>
    [SlashCommand("rank", "Zeigt dein Level und XP")]
    public async Task RankAsync()
    {
        await DeferAsync(ephemeral: true).ConfigureAwait(false);
        var guildId = Context.Guild?.Id ?? 0;
        var profile = await _repository.GetByUserAsync(Context.User.Id, guildId).ConfigureAwait(false);
        var xp = profile?.Xp ?? 0;
        var level = profile?.Level ?? 0;
        var voice = profile?.VoiceMinutes ?? 0;
        await FollowupAsync($"Level {level} — {xp} XP ({voice} Voice-Min)", ephemeral: true).ConfigureAwait(false);
    }

    /// <summary>Shows the XP leaderboard.</summary>
    [SlashCommand("leaderboard", "Zeigt das XP-Leaderboard")]
    public async Task LeaderboardAsync(
        [Summary("scope", "server, channel oder voice")] string scope = "server",
        [Summary("limit", "Anzahl der Einträge")] int limit = 10)
    {
        await DeferAsync().ConfigureAwait(false);

        var guildId = Context.Guild?.Id ?? 0;
        IReadOnlyList<XpProfile> top;
        if (string.Equals(scope, "voice", StringComparison.OrdinalIgnoreCase))
        {
            top = await _repository.GetVoiceLeaderboardAsync(guildId, limit).ConfigureAwait(false);
        }
        else
        {
            top = await _repository.GetLeaderboardAsync(guildId, limit).ConfigureAwait(false);
        }

        if (top.Count == 0)
        {
            await FollowupAsync("Noch keine XP-Einträge.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        var builder = new System.Text.StringBuilder();
        builder.AppendLine("**🏆 XP-Leaderboard**");
        var rank = 1;
        foreach (var entry in top)
        {
            builder.AppendLine($"{rank}. <@{entry.DiscordUserId}> — Level {entry.Level} ({entry.Xp} XP, {entry.VoiceMinutes} Voice-Min)");
            rank++;
        }

        await FollowupAsync(builder.ToString().TrimEnd(), ephemeral: true).ConfigureAwait(false);
    }

    /// <summary>Resets a user's XP.</summary>
    [SlashCommand("reset", "Setzt die XP eines Users zurück (Admin)")]
    [RequireUserPermission(GuildPermission.Administrator)]
    public async Task ResetAsync([Summary("user", "Der User")] IUser user)
    {
        await DeferAsync(ephemeral: true).ConfigureAwait(false);
        var guildId = Context.Guild?.Id ?? 0;
        await _repository.ResetAsync(user.Id, guildId).ConfigureAwait(false);
        await FollowupAsync($"✅ XP für <@{user.Id}> zurückgesetzt.", ephemeral: true).ConfigureAwait(false);
    }

    /// <summary>Sets a user's level.</summary>
    [SlashCommand("set", "Setzt das Level eines Users (Admin)")]
    [RequireUserPermission(GuildPermission.Administrator)]
    public async Task SetAsync([Summary("user", "Der User")] IUser user, [Summary("level", "Level")] int level)
    {
        await DeferAsync(ephemeral: true).ConfigureAwait(false);
        var guildId = Context.Guild?.Id ?? 0;
        await _repository.SetLevelAsync(user.Id, guildId, level).ConfigureAwait(false);
        await FollowupAsync($"✅ <@{user.Id}> ist jetzt Level {level}.", ephemeral: true).ConfigureAwait(false);
    }
}
