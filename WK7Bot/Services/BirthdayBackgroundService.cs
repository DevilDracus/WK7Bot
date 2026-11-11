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
/// Announces member birthdays once per year to the configured birthday channel.
/// </summary>
public class BirthdayBackgroundService : BackgroundService
{
    private static readonly TimeSpan Tick = TimeSpan.FromHours(1);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly DiscordSocketClient _discordClient;
    private readonly IFeatureHealthTracker _health;
    private readonly Wk7BotOptions _options;
    private readonly ILogger<BirthdayBackgroundService> _logger;

    /// <summary>Initializes a new instance of the <see cref="BirthdayBackgroundService"/> class.</summary>
    /// <param name="scopeFactory">The scope factory used for the birthday repository.</param>
    /// <param name="discordClient">The connected Discord socket client.</param>
    /// <param name="health">The feature health tracker.</param>
    /// <param name="options">The strongly-typed bot options.</param>
    /// <param name="logger">The logger instance.</param>
    public BirthdayBackgroundService(
        IServiceScopeFactory scopeFactory,
        DiscordSocketClient discordClient,
        IFeatureHealthTracker health,
        IOptions<Wk7BotOptions> options,
        ILogger<BirthdayBackgroundService> logger)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _discordClient = discordClient ?? throw new ArgumentNullException(nameof(discordClient));
        _health = health ?? throw new ArgumentNullException(nameof(health));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Features.BirthdayEnabled)
        {
            _logger.LogInformation("Birthday background service is disabled via feature options.");
            return;
        }

        _logger.LogInformation("Birthday background service started.");

        await BackgroundServiceResilience.RunWithResilienceAsync(
            async token => await EvaluateAsync(DateTime.UtcNow, token),
            FeatureKeys.Birthdays,
            _health,
            _logger,
            Tick,
            maxConsecutiveFailures: BackgroundServiceResilience.DefaultMaxConsecutiveFailures,
            restartDelay: BackgroundServiceResilience.DefaultRestartDelay,
            stoppingToken);
    }

    /// <summary>
    /// Checks for birthdays falling on today's date and announces them once per year.
    /// </summary>
    /// <param name="now">The current UTC time.</param>
    /// <param name="stoppingToken">Cancellation token.</param>
    /// <returns>A task representing the evaluation.</returns>
    internal async Task EvaluateAsync(DateTime now, CancellationToken stoppingToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IBirthdayRepository>();

        var guildIds = GetTargetGuildIds().ToList();
        if (guildIds.Count == 0)
        {
            return;
        }

        var due = await repository.GetDueTodayAsync(now.Month, now.Day, now.Year, stoppingToken);
        foreach (var birthday in due)
        {
            foreach (var guildId in guildIds)
            {
                var channel = await GetOrCreateBirthdayChannelAsync(guildId);
                if (channel is null)
                {
                    _logger.LogWarning("Could not resolve or create the birthday channel for guild {GuildId}.", guildId);
                    continue;
                }

                await channel.SendMessageAsync($"🎂 <@{birthday.DiscordUserId}> hat heute Geburtstag! Alles Gute! 🎉").ConfigureAwait(false);
            }

            await repository.MarkAnnouncedAsync(birthday.Id, now.Year, stoppingToken).ConfigureAwait(false);
        }
    }

    /// <summary>Resolves (or creates) the configured birthday channel in a guild.</summary>
    /// <param name="guildId">The target guild.</param>
    /// <returns>The channel, or <see langword="null"/>.</returns>
    protected virtual Task<ITextChannel?> GetOrCreateBirthdayChannelAsync(ulong guildId)
    {
        var guild = _discordClient.GetGuild(guildId);
        return guild is null
            ? Task.FromResult<ITextChannel?>(null)
            : ChannelResolver.TryGetOrCreateFeatureChannelAsync(
                guild,
                _discordClient.CurrentUser?.Id ?? 0,
                _options.BirthdayChannelName,
                "Geburtstagsankündigungen.",
                _logger);
    }

    /// <summary>Returns the IDs of the guilds that should receive birthday posts.</summary>
    /// <returns>The target Discord guild IDs.</returns>
    protected virtual IReadOnlyList<ulong> GetTargetGuildIds()
        => AutomaticTargetResolver.Resolve(
            _options.Servers,
            FeatureKeys.RoutedFeatures.Birthdays,
            _discordClient.Guilds.Select(guild => guild.Id),
            _logger);
}
