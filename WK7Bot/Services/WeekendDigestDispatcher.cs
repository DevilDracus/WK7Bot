using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;
using WK7Bot.Core.Interfaces;
using WK7Bot.Core.Utilities;

namespace WK7Bot.Services;

/// <summary>
/// Handles the per-guild dispatch of the weekend digest: resolves targets, creates channels when missing,
/// posts the embed (and optional poll), and records dispatch state so restarts do not duplicate messages.
/// </summary>
public class WeekendDigestDispatcher
{
    private const string TargetChannelName = "📅wochenende";
    private readonly DiscordSocketClient _discordClient;
    private readonly ILogger<WeekendDigestDispatcher> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="WeekendDigestDispatcher"/> class.
    /// </summary>
    /// <param name="discordClient">The connected Discord socket client.</param>
    /// <param name="logger">Logger for dispatch diagnostics.</param>
    public WeekendDigestDispatcher(DiscordSocketClient discordClient, ILogger<WeekendDigestDispatcher> logger)
    {
        _discordClient = discordClient ?? throw new ArgumentNullException(nameof(discordClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Posts the digest to every guild that has not yet received it for the given Thursday.
    /// </summary>
    /// <param name="thursday">The Thursday date identifying the dispatch.</param>
    /// <param name="digest">The pre-built digest embed and optional poll.</param>
    /// <param name="dispatchRepository">The repository used to track per-guild dispatch state.</param>
    /// <param name="targetGuildIds">The list of guild IDs that should receive the digest.</param>
    /// <param name="cancellationToken">Cancellation token for network and database operations.</param>
    /// <returns>The number of guilds the digest was successfully posted to.</returns>
    public async Task<int> DispatchAsync(
        DateTime thursday,
        (Embed Embed, PollProperties? Poll) digest,
        IWasteDispatchRepository dispatchRepository,
        IReadOnlyList<ulong> targetGuildIds,
        CancellationToken cancellationToken)
    {
        var pendingGuildIds = new List<ulong>();
        foreach (var guildId in targetGuildIds)
        {
            if (!await dispatchRepository.HasSentAsync(WeekendDigestBackgroundService.DispatchKind, guildId, thursday, cancellationToken))
            {
                pendingGuildIds.Add(guildId);
            }
        }

        if (pendingGuildIds.Count == 0)
        {
            _logger.LogInformation(
                "Skipping weekend digest dispatch for {Thursday:dd.MM.yyyy}: already sent to all {Count} guild(s) before a restart.",
                thursday,
                targetGuildIds.Count);
            return 0;
        }

        int dispatchedCount = 0;
        foreach (var guildId in pendingGuildIds)
        {
            var channel = await GetOrCreateWeekendChannelAsync(guildId);
            if (channel == null)
            {
                _logger.LogWarning("Could not resolve or create the weekend channel for guild {GuildId}; skipping.", guildId);
                continue;
            }

            await PostToWeekendChannelAsync(channel, digest.Embed, digest.Poll);
            dispatchedCount++;

            // Record only after a successful send so a crash or failure retries this guild next attempt
            // while guilds that already received the digest stay suppressed.
            await dispatchRepository.MarkSentAsync(WeekendDigestBackgroundService.DispatchKind, guildId, thursday, cancellationToken);
        }

        _logger.LogDebug(
            "Dispatched weekend digest for {Thursday:dd.MM.yyyy} to {Count} guild(s).",
            thursday,
            dispatchedCount);

        return dispatchedCount;
    }

    /// <summary>
    /// Resolves the weekend digest channel for a guild by ID, creating a new channel when missing.
    /// </summary>
    /// <param name="guildId">The target guild where channel existence is evaluated.</param>
    /// <returns>The text channel instance, or <see langword="null"/> when the guild cannot be resolved.</returns>
    protected virtual async Task<ITextChannel?> GetOrCreateWeekendChannelAsync(ulong guildId)
    {
        var guild = _discordClient.GetGuild(guildId);
        if (guild is null)
        {
            return null;
        }

        return await ChannelResolver.TryGetOrCreateFeatureChannelAsync(
            guild,
            _discordClient.CurrentUser.Id,
            TargetChannelName,
            "Wochenend-Tipps aus Leipzig – Märkte, Kultur und Ausflüge mit Abstimmung.",
            _logger,
            allowReactions: true);
    }

    /// <summary>
    /// Posts the constructed digest to the resolved weekend channel.
    /// </summary>
    /// <param name="channel">The channel that receives the digest.</param>
    /// <param name="embed">The digest embed.</param>
    /// <param name="poll">The poll to attach, or <see langword="null"/> when there is a single suggestion.</param>
    /// <returns>A task that completes once the message has been sent.</returns>
    protected virtual Task PostToWeekendChannelAsync(ITextChannel channel, Embed embed, PollProperties? poll)
        => poll is null
            ? channel.SendMessageAsync(embed: embed)
            : channel.SendMessageAsync(embed: embed, poll: poll);
}
