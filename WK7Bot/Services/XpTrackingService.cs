namespace WK7Bot.Services;

using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using WK7Bot.Core.Entities;
using WK7Bot.Core.Interfaces;
using WK7Bot.Core.Utilities;
using WK7Bot.Options;

/// <summary>
/// Tracks message-based XP, awards voice XP is handled separately, and announces level-ups.
/// </summary>
public class XpTrackingService : IHostedService
{
    private readonly DiscordSocketClient _discordClient;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptions<Wk7BotOptions> _options;
    private readonly FeatureGate _featureGate;
    private readonly ILogger<XpTrackingService> _logger;
    private readonly Dictionary<ulong, (string Content, DateTime At)> _recentMessages = new();
    private readonly Random _random = new();

    /// <summary>Cooldown between XP awards for the same user.</summary>
    private static readonly TimeSpan XpCooldown = TimeSpan.FromSeconds(60);

    /// <summary>Window in which identical messages are treated as duplicates.</summary>
    private static readonly TimeSpan DuplicateWindow = TimeSpan.FromSeconds(5);

    /// <summary>Initializes a new instance.</summary>
    public XpTrackingService(
        DiscordSocketClient discordClient,
        IServiceScopeFactory scopeFactory,
        IOptions<Wk7BotOptions> options,
        FeatureGate featureGate,
        ILogger<XpTrackingService> logger)
    {
        _discordClient = discordClient ?? throw new ArgumentNullException(nameof(discordClient));
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _featureGate = featureGate ?? throw new ArgumentNullException(nameof(featureGate));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _discordClient.MessageReceived += OnMessageReceivedAsync;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        _discordClient.MessageReceived -= OnMessageReceivedAsync;
        return Task.CompletedTask;
    }

    private async Task OnMessageReceivedAsync(SocketMessage message)
    {
        try
        {
            if (!_featureGate.IsEnabled(FeatureKeys.Xp))
            {
                return;
            }

            if (message.Author.IsBot)
            {
                return;
            }

            if (message.Content is null || message.Content.Trim().Length < 3)
            {
                return;
            }

            if (message.Channel is not SocketGuildChannel guildChannel)
            {
                return;
            }

            var guild = guildChannel.Guild;
            var userId = message.Author.Id;
            var now = DateTime.UtcNow;
            var content = message.Content.Trim();

            // Duplicate detection
            if (_recentMessages.TryGetValue(userId, out var recent)
                && recent.Content == content
                && now - recent.At < DuplicateWindow)
            {
                return;
            }

            _recentMessages[userId] = (content, now);

            using var scope = _scopeFactory.CreateScope();
            var repository = scope.ServiceProvider.GetRequiredService<IXpRepository>();

            var profile = await repository.GetOrCreateAsync(userId, guild.Id).ConfigureAwait(false);

            if (profile.LastXpAt != DateTime.MinValue && now - profile.LastXpAt < XpCooldown)
            {
                return;
            }

            var xpGain = _random.Next(15, 26);
            profile.Xp += xpGain;
            profile.LastXpAt = now;
            var newLevel = (int)Math.Floor(Math.Sqrt(profile.Xp / 100.0));
            var leveledUp = newLevel > profile.Level;
            profile.Level = newLevel;

            await repository.UpsertAsync(profile).ConfigureAwait(false);

            if (leveledUp)
            {
                await AnnounceLevelUpAsync(guild, message.Author, profile, newLevel).ConfigureAwait(false);
                await GrantLevelRolesAsync(guild, message.Author, newLevel).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to process XP for message.");
        }
    }

    private async Task AnnounceLevelUpAsync(SocketGuild guild, IUser user, XpProfile profile, int newLevel)
    {
        var channel = guild.TextChannels.FirstOrDefault(c =>
            c.Name.Contains("leaderboard", StringComparison.OrdinalIgnoreCase));
        if (channel is null)
        {
            return;
        }

        try
        {
            await channel.SendMessageAsync($"🎉 <@{user.Id}> reached level {newLevel}! (XP: {profile.Xp})").ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to announce level-up.");
        }
    }

    private async Task GrantLevelRolesAsync(SocketGuild guild, IUser user, int newLevel)
    {
        var mappings = _options.Value.XpLevelRoles;
        if (mappings is null || mappings.Count == 0)
        {
            return;
        }

        SocketGuildUser? guildUser = null;
        try
        {
            guildUser = guild.GetUser(user.Id);
        }
        catch
        {
            return;
        }

        if (guildUser is null)
        {
            return;
        }

        foreach (var (level, roleId) in mappings)
        {
            if (newLevel < level)
            {
                continue;
            }

            if (guildUser.Roles.Any(r => r.Id == roleId))
            {
                continue;
            }

            var role = guild.GetRole(roleId);
            if (role is null)
            {
                continue;
            }

            try
            {
                await guildUser.AddRoleAsync(role).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to grant level role {RoleId}.", roleId);
            }
        }
    }
}
