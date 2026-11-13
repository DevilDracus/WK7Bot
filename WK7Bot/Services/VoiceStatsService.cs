namespace WK7Bot.Services;

using System.Collections.Concurrent;
using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using WK7Bot.Core.Entities;
using WK7Bot.Core.Interfaces;
using WK7Bot.Core.Utilities;
using WK7Bot.Options;

/// <summary>
/// Tracks voice-channel sessions and records them for later reporting.
/// </summary>
public class VoiceStatsService : IHostedService
{
    private readonly DiscordSocketClient _discordClient;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<VoiceStatsService> _logger;
    private readonly ConcurrentDictionary<ulong, (ulong GuildId, ulong ChannelId, DateTime JoinedAt)> _active = new();

    /// <summary>Minimum voice session length (in seconds) worth recording; shorter sessions are ignored as noise.</summary>
    private const int MinimumSessionDurationSeconds = 5;

    /// <summary>Initializes a new instance of the <see cref="VoiceStatsService"/> class.</summary>
    /// <param name="discordClient">The connected Discord socket client.</param>
    /// <param name="scopeFactory">The scope factory used for the repository.</param>
    /// <param name="logger">The logger instance.</param>
    public VoiceStatsService(DiscordSocketClient discordClient, IServiceScopeFactory scopeFactory, ILogger<VoiceStatsService> logger)
    {
        _discordClient = discordClient ?? throw new ArgumentNullException(nameof(discordClient));
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _discordClient.UserVoiceStateUpdated += OnVoiceStateUpdatedAsync;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        _discordClient.UserVoiceStateUpdated -= OnVoiceStateUpdatedAsync;
        return Task.CompletedTask;
    }

    /// <summary>Handles voice state updates for stats tracking.</summary>
    /// <param name="user">The user.</param>
    /// <param name="before">The previous state.</param>
    /// <param name="after">The new state.</param>
    /// <returns>A task tracking the operation.</returns>
    private async Task OnVoiceStateUpdatedAsync(SocketUser user, SocketVoiceState before, SocketVoiceState after)
    {
        try
        {
            var userId = user.Id;

            // User joined a channel (was not in voice, now is)
            if (before.VoiceChannel is null && after.VoiceChannel is not null)
            {
                _active[userId] = (after.VoiceChannel.Guild.Id, after.VoiceChannel.Id, DateTime.UtcNow);
                return;
            }

            // User left voice entirely
            if (before.VoiceChannel is not null && after.VoiceChannel is null)
            {
                if (_active.TryGetValue(userId, out var session))
                {
                    await RecordSessionAsync(userId, session, DateTime.UtcNow).ConfigureAwait(false);
                    _active.TryRemove(userId, out _);
                }
                return;
            }

            // User moved between channels: close previous, open new
            if (before.VoiceChannel is not null && after.VoiceChannel is not null
                && before.VoiceChannel.Id != after.VoiceChannel.Id)
            {
                if (_active.TryGetValue(userId, out var session))
                {
                    await RecordSessionAsync(userId, session, DateTime.UtcNow).ConfigureAwait(false);
                }

                _active[userId] = (after.VoiceChannel.Guild.Id, after.VoiceChannel.Id, DateTime.UtcNow);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to process voice state update for user {UserId}.", user.Id);
        }
    }

    /// <summary>Records a completed voice session if it exceeds the minimum duration.</summary>
    /// <param name="userId">The user id.</param>
    /// <param name="session">The session data.</param>
    /// <param name="leftAt">When the user left.</param>
    /// <returns>A task tracking the operation.</returns>
    private async Task RecordSessionAsync(ulong userId, (ulong GuildId, ulong ChannelId, DateTime JoinedAt) session, DateTime leftAt)
    {
        var duration = (int)(leftAt - session.JoinedAt).TotalSeconds;
        if (duration < MinimumSessionDurationSeconds)
        {
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IVoiceStatsRepository>();

        await repository.AddAsync(new VoiceSession
        {
            DiscordUserId = userId,
            GuildId = session.GuildId,
            ChannelId = session.ChannelId,
            JoinedAt = session.JoinedAt,
            LeftAt = leftAt,
            DurationSeconds = duration
        }).ConfigureAwait(false);

        // Award 1 XP per minute spent in voice.
        var featureGate = scope.ServiceProvider.GetRequiredService<FeatureGate>();
        if (featureGate.IsEnabled(FeatureKeys.Xp))
        {
            var xpRepository = scope.ServiceProvider.GetRequiredService<IXpRepository>();
            var minutes = duration / 60;
            if (minutes > 0)
            {
                var profile = await xpRepository.GetOrCreateAsync(userId, session.GuildId).ConfigureAwait(false);
                var oldLevel = profile.Level;
                profile.Xp += minutes;
                profile.VoiceMinutes += minutes;
                profile.LastXpAt = DateTime.UtcNow;
                var newLevel = (int)Math.Floor(Math.Sqrt(profile.Xp / 100.0));
                var leveledUp = newLevel > profile.Level;
                profile.Level = newLevel;
                await xpRepository.UpsertAsync(profile).ConfigureAwait(false);

                if (leveledUp)
                {
                    var options = scope.ServiceProvider.GetRequiredService<IOptions<Wk7BotOptions>>();
                    var guild = _discordClient.GetGuild(session.GuildId);
                    if (guild is not null)
                    {
                        var channel = guild.TextChannels.FirstOrDefault(c => c.Name.Contains("leaderboard", StringComparison.OrdinalIgnoreCase));
                        if (channel is not null)
                        {
                            try { await channel.SendMessageAsync($"🎉 <@{userId}> reached level {newLevel}! (XP: {profile.Xp})").ConfigureAwait(false); } catch { }
                        }

                        var mappings = options.Value.XpLevelRoles;
                        if (mappings is not null && mappings.Count > 0)
                        {
                            var guildUser = guild.GetUser(userId);
                            if (guildUser is not null)
                            {
                                foreach (var (level, roleId) in mappings)
                                {
                                    if (newLevel < level || guildUser.Roles.Any(r => r.Id == roleId)) continue;
                                    var role = guild.GetRole(roleId);
                                    if (role is null) continue;
                                    try { await guildUser.AddRoleAsync(role).ConfigureAwait(false); } catch { }
                                }

                                // Remove lower-level roles so they do not stack.
                                foreach (var (level, roleId) in mappings)
                                {
                                    if (level >= newLevel || !guildUser.Roles.Any(r => r.Id == roleId)) continue;
                                    var role = guild.GetRole(roleId);
                                    if (role is null) continue;
                                    try { await guildUser.RemoveRoleAsync(role).ConfigureAwait(false); } catch { }
                                }
                            }
                        }
                    }
                }
            }
        }
    }

}
