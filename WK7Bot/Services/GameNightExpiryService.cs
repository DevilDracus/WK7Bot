#pragma warning disable CS1591
#pragma warning disable CS1587
namespace WK7Bot.Services;

using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using WK7Bot.Core.Entities;
using WK7Bot.Core.Interfaces;
using WK7Bot.Core.Utilities;
using WK7Bot.Services.Interfaces;
using WK7Bot.Options;

public class GameNightExpiryService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly DiscordSocketClient _discordClient;
    private readonly IFeatureHealthTracker _health;
    private readonly Wk7BotOptions _options;
    private readonly ILogger<GameNightExpiryService> _logger;

    public GameNightExpiryService(
        IServiceScopeFactory scopeFactory,
        DiscordSocketClient discordClient,
        IFeatureHealthTracker health,
        IOptions<Wk7BotOptions> options,
        ILogger<GameNightExpiryService> logger)
    {
        _scopeFactory = scopeFactory;
        _discordClient = discordClient;
        _health = health;
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Features.GameNightEnabled)
        {
            _logger.LogInformation("Game Night expiry service is disabled via feature options.");
            return;
        }

        _logger.LogInformation("Starting Game Night expiry scheduler loop.");

        await BackgroundServiceResilience.RunWithResilienceAsync(
            async ct => { if (await SweepAsync(DateTime.UtcNow, ct)) _health.RecordSuccess(FeatureKeys.GameNight); },
            FeatureKeys.GameNight,
            _health,
            _logger,
            TimeSpan.FromMinutes(1),
            stoppingToken: stoppingToken).ConfigureAwait(false);
    }

    protected virtual async Task<bool> SweepAsync(DateTime now, CancellationToken stoppingToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IGameNightRepository>();
        var reminderRepository = scope.ServiceProvider.GetService<IReminderRepository>();
        var scheduledEventService = scope.ServiceProvider.GetService<IDiscordScheduledEventService>();

        var expired = await repository.GetExpiredAsync(now, stoppingToken);
        if (expired.Count == 0) return true;

        await repository.CloseManyAsync(expired.Select(m => m.Id), stoppingToken).ConfigureAwait(false);
        foreach (var gn in expired) gn.Closed = true;

        var allClean = true;
        foreach (var gn in expired)
        {
            stoppingToken.ThrowIfCancellationRequested();
            try
            {
                var slots = await repository.GetSlotsAsync(gn.Id, stoppingToken);
                var votes = await repository.GetVotesAsync(gn.Id, stoppingToken);

                GameNightSlot? best = null;
                int bestCount = -1;
                foreach (var slot in slots.OrderBy(s => s.SlotTime ?? DateTime.MaxValue).ThenBy(s => s.Position))
                {
                    var count = votes.Count(v => v.SlotId == slot.Id && v.Available);
                    if (count > bestCount) { bestCount = count; best = slot; }
                }
                if (best != null)
                {
                    await repository.SetBestSlotAsync(gn.Id, best.Id, stoppingToken);
                    gn.BestSlot = best.Label;
                    gn.BestSlotId = best.Id;
                }

                // edit message
                if (gn.MessageId != 0 && _discordClient.GetChannel(gn.ChannelId) is IMessageChannel channel)
                {
                    if (await channel.GetMessageAsync(gn.MessageId) is IUserMessage msg)
                    {
                        var (embed, components) = GameNightMessageBuilder.Build(gn, slots, votes, now);
                        await EditMeetupMessageAsync(gn, embed, components);
                    }
                }

                // native event
                if (best != null && scheduledEventService != null)
                {
                    var guild = _discordClient.GetGuild(gn.GuildId);
                    if (guild != null)
                    {
                        DateTime start = best.SlotTime ?? gn.ClosesAt;
                        if (start < DateTime.UtcNow) start = DateTime.UtcNow.AddMinutes(5);
                        var end = start.AddHours(2);
                        try
                        {
                            await scheduledEventService.CreateAsync(guild, $"Game Night: {gn.Title ?? "Spieleabend"}", $"Slot: {best.Label}", best.Label, new DateTimeOffset(start), new DateTimeOffset(end), "game_night");
                        }
                        catch (Exception ex) { _logger.LogWarning(ex, "Failed to create scheduled event for game night {GameNightId}.", gn.Id); }
                    }
                }

                // reminders
                if (best != null && reminderRepository != null)
                {
                    DateTime start = best.SlotTime ?? gn.ClosesAt;
                    var due = start.AddHours(-1);
                    if (due < DateTime.UtcNow) due = DateTime.UtcNow.AddMinutes(1);
                    var voters = votes.Where(v => v.SlotId == best.Id && v.Available).Select(v => v.UserId).Distinct();
                    foreach (var uid in voters)
                    {
                        try
                        {
                            await reminderRepository.AddAsync(new Reminder
                            {
                                DiscordUserId = uid,
                                GuildId = gn.GuildId,
                                ChannelId = gn.ChannelId,
                                Text = $"Game Night '{gn.Title ?? "Spieleabend"}' startet um {start:HH:mm} Uhr (Slot: {best.Label}).",
                                DueAt = due,
                                CreatedAt = DateTime.UtcNow
                            }, stoppingToken);
                        }
                        catch (Exception ex) { _logger.LogWarning(ex, "Failed to add reminder for game night {GameNightId}.", gn.Id); }
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
            {
                allClean = false;
                _logger.LogWarning(ex, "Could not close game night {Id}.", gn.Id);
                _health.RecordFailure(FeatureKeys.GameNight, ex.Message);
            }
        }

        return allClean;
    }

    protected virtual Task EditMeetupMessageAsync(GameNight gameNight, Embed embed, MessageComponent components)
        => EditGameNightMessageAsync(gameNight, embed, components);

    protected virtual async Task EditGameNightMessageAsync(GameNight gameNight, Embed embed, MessageComponent components)
    {
        if (_discordClient.GetChannel(gameNight.ChannelId) is not IMessageChannel channel) return;
        if (await channel.GetMessageAsync(gameNight.MessageId) is not IUserMessage message) return;
        await message.ModifyAsync(props =>
        {
            props.Embed = embed;
            props.Components = components;
        });
    }

}
