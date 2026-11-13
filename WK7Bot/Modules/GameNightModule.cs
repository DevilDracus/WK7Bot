#pragma warning disable CS1591
#pragma warning disable CS1587
namespace WK7Bot.Modules;

using System.Net;
using Discord;
using Discord.Interactions;
using Discord.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WK7Bot.Core.Entities;
using WK7Bot.Core.Interfaces;
using WK7Bot.Core.Utilities;
using WK7Bot.Options;
using WK7Bot.Services.Interfaces;

[Group("gamenight", "Befehle zum Planen eines Spieleabends mit Verfügbarkeitsabfrage.")]
public class GameNightModule : BotModuleBase
{
    private const int AutoCloseHours = 24;

    private readonly IGameNightRepository _repository;
    private readonly IDiscordScheduledEventService _scheduledEventService;
    private readonly IReminderRepository? _reminderRepository;
    private readonly IOptions<ServersOptions>? _serversOptions;
    private readonly ILogger<GameNightModule> _logger;

    protected override ILogger Logger => _logger;

    public GameNightModule(
        IGameNightRepository repository,
        IDiscordScheduledEventService scheduledEventService,
        IReminderRepository? reminderRepository,
        IOptions<ServersOptions>? serversOptions,
        ILogger<GameNightModule> logger)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _scheduledEventService = scheduledEventService ?? throw new ArgumentNullException(nameof(scheduledEventService));
        _reminderRepository = reminderRepository;
        _serversOptions = serversOptions;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected virtual Task<ITextChannel?> GetChannelAsync()
        => GetTreffChannelAsync();

    protected virtual Task<ITextChannel?> GetTreffChannelAsync()
        => Task.FromResult(Context.Channel as ITextChannel);

    protected virtual Task<IUserMessage> PostAsync(ITextChannel channel, Embed embed, MessageComponent components)
        => PostToTreffChannelAsync(channel, string.Empty, embed, components);

    protected virtual Task<IUserMessage> PostToTreffChannelAsync(ITextChannel channel, string text, Embed embed, MessageComponent components)
        => channel.SendMessageAsync(text, embed: embed, components: components);

    private bool IsAllowedGuild()
    {
        if (_serversOptions?.Value is { } opts && opts.TryGetWk7ServerId(out var wk7))
        {
            return Context.Guild?.Id == wk7;
        }
        return true;
    }

    [SlashCommand("create", "Erstellt einen Spieleabend mit Verfügbarkeitsabfrage.")]
    public async Task CreateAsync(
        [Summary("slots", "Kommagetrennte Slot-Liste, z. B. 'Fri 20:00, Sat 19:00' (optional)")] string? slots = null,
        [Summary("title", "Titel des Spieleabends (optional)")] string? title = null)
    {
        await DeferAsync(ephemeral: true).ConfigureAwait(false);

        if (!IsAllowedGuild())
        {
            await FollowupAsync("❌ Dieser Befehl ist nur auf dem WK7-Server verfügbar.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        if (Context.Guild == null)
        {
            await FollowupAsync("❌ Diese Aktion ist nur innerhalb eines Servers möglich.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        try
        {
            var slotLabels = ParseSlots(slots);
            var gameNight = await _repository.AddAsync(new GameNight
            {
                GuildId = Context.Guild.Id,
                ChannelId = Context.Channel.Id,
                OrganizerId = Context.User.Id,
                OrganizerName = Context.User.Username,
                Title = string.IsNullOrWhiteSpace(title) ? null : title!.Trim(),
                CreatedAt = DateTime.UtcNow,
                ClosesAt = DateTime.UtcNow.AddHours(AutoCloseHours)
            });

            if (slotLabels.Count > 0)
            {
                await _repository.AddSlotsAsync(gameNight.Id, slotLabels);
            }

            var slotsList = await _repository.GetSlotsAsync(gameNight.Id);
            var (embed, components) = GameNightMessageBuilder.Build(gameNight, slotsList, DateTime.UtcNow);

            try
            {
                var channel = await GetTreffChannelAsync();
                if (channel == null)
                {
                    await FollowupAsync("❌ Kanal nicht gefunden.", ephemeral: true).ConfigureAwait(false);
                    return;
                }
                var message = await PostToTreffChannelAsync(channel, string.Empty, embed, components);
                await _repository.SetMessageAsync(gameNight.Id, channel.Id, message.Id).ConfigureAwait(false);

                await FollowupAsync($"✅ Game Night erstellt: [Zur Umfrage](https://discord.com/channels/{gameNight.GuildId}/{channel.Id}/{message.Id})", ephemeral: true);
            }
            catch (Exception)
            {
                try { await _repository.RemoveAsync(gameNight.Id); } catch { }
                throw;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || ex is TaskCanceledException)
        {
            _logger.LogError(ex, "Error occurred while executing /gamenight create.");
            await FollowupAsync("❌ Der Spieleabend konnte nicht erstellt werden.", ephemeral: true).ConfigureAwait(false);
        }
    }

    [SlashCommand("reschedule", "Setzt den Spieleabend auf einen anderen Slot (Organizer-Override).")]
    public async Task RescheduleAsync(
        [Summary("slot", "Der gewünschte Slot-Text")] string slot,
        [Summary("gamenight", "ID der Game Night (optional)")] int gameNightId = 0)
    {
        await DeferAsync(ephemeral: true).ConfigureAwait(false);
        if (!IsAllowedGuild()) { await FollowupAsync("❌ Nur auf WK7-Server.", ephemeral: true); return; }

        try
        {
            GameNight? gn = null;
            if (gameNightId > 0) gn = await _repository.GetAsync(gameNightId);
            else
            {
                // find latest open game night in channel
                // not tracked; require id
            }
            if (gn == null)
            {
                await FollowupAsync("❌ Game Night nicht gefunden.", ephemeral: true).ConfigureAwait(false);
                return;
            }
            if (gn.OrganizerId != Context.User.Id)
            {
                await FollowupAsync("❌ Nur der Organisator darf verschieben.", ephemeral: true).ConfigureAwait(false);
                return;
            }

            var slots = await _repository.GetSlotsAsync(gn.Id);
            var target = slots.FirstOrDefault(s => string.Equals(s.Label, slot.Trim(), StringComparison.OrdinalIgnoreCase));
            if (target == null)
            {
                // add as new slot
                var added = await _repository.AddSlotAsync(new GameNightSlot { GameNightId = gn.Id, Label = slot.Trim(), Position = slots.Count });
                target = added;
            }
            await _repository.SetBestSlotAsync(gn.Id, target.Id);
            gn.BestSlot = target.Label;
            gn.Closed = true;
            // mark closed
            await _repository.CloseAsync(gn.Id);
            await FollowupAsync($"✅ Spieleabend auf '{target.Label}' verschoben.", ephemeral: true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || ex is TaskCanceledException)
        {
            _logger.LogError(ex, "Error rescheduling game night.");
            await FollowupAsync("❌ Verschieben fehlgeschlagen.", ephemeral: true);
        }
    }

    // overload compatibility
    private Task RescheduleAsync(int gameNightId, string slot)
        => RescheduleAsync(slot, gameNightId);

    [SlashCommand("close", "Schließt die Game Night vorzeitig.")]
    public async Task CloseAsync(
        [Summary("gamenight", "ID der Game Night (optional)")] int gameNightId = 0)
    {
        await DeferAsync(ephemeral: true).ConfigureAwait(false);
        if (!IsAllowedGuild()) { await FollowupAsync("❌ Nur auf WK7-Server.", ephemeral: true); return; }

        try
        {
            GameNight? gn = null;
            if (gameNightId > 0) gn = await _repository.GetAsync(gameNightId);
            if (gn == null)
            {
                await FollowupAsync("❌ Game Night nicht gefunden.", ephemeral: true).ConfigureAwait(false);
                return;
            }
            if (gn.OrganizerId != Context.User.Id)
            {
                await FollowupAsync("❌ Nur der Organisator darf schließen.", ephemeral: true).ConfigureAwait(false);
                return;
            }

            await CloseGameNightAsync(gn);
            await FollowupAsync("✅ Game Night geschlossen.", ephemeral: true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || ex is TaskCanceledException)
        {
            _logger.LogError(ex, "Error closing game night.");
            await FollowupAsync("❌ Schließen fehlgeschlagen.", ephemeral: true);
        }
    }

    // wizard button/modal could be added; placeholder no-op
    private static List<string> ParseSlots(string? slots)
    {
        if (string.IsNullOrWhiteSpace(slots)) return new List<string>();
        return slots.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToList();
    }

    private async Task CloseGameNightAsync(GameNight gn)
    {
        var slots = await _repository.GetSlotsAsync(gn.Id);
        var votes = await _repository.GetVotesAsync(gn.Id);

        // pick best slot: max available voters, tie -> earliest slot
        GameNightSlot? best = null;
        int bestCount = -1;
        foreach (var slot in slots.OrderBy(s => s.SlotTime ?? DateTime.MaxValue).ThenBy(s => s.Position))
        {
            var count = votes.Count(v => v.SlotId == slot.Id && v.Available);
            if (count > bestCount)
            {
                bestCount = count;
                best = slot;
            }
        }

        if (best != null)
        {
            await _repository.SetBestSlotAsync(gn.Id, best.Id);
            gn.BestSlot = best.Label;
            gn.BestSlotId = best.Id;
        }

        await _repository.CloseAsync(gn.Id);
        gn.Closed = true;

        // edit message
        if (gn.MessageId != 0 && Context.Channel is IMessageChannel ch)
        {
            try
            {
                if (await ch.GetMessageAsync(gn.MessageId) is IUserMessage msg)
                {
                    var (embed, components) = GameNightMessageBuilder.Build(gn, slots, votes, DateTime.UtcNow);
                    await msg.ModifyAsync(p => { p.Embed = embed; p.Components = components; });
                }
            }
            catch { }
        }

        // create native event
        if (best != null && Context.Guild != null)
        {
            DateTime start = best.SlotTime ?? gn.ClosesAt;
            if (start < DateTime.UtcNow) start = DateTime.UtcNow.AddMinutes(5);
            var end = start.AddHours(2);
            try
            {
                await _scheduledEventService.CreateAsync(
                    Context.Guild,
                    $"Game Night: {gn.Title ?? "Spieleabend"}",
                    $"Slot: {best.Label}",
                    best.Label,
                    new DateTimeOffset(start),
                    new DateTimeOffset(end),
                    "game_night");
            }
            catch { }
        }

        // reminders via ReminderRepository
        if (best != null && _reminderRepository != null)
        {
            DateTime start = best.SlotTime ?? gn.ClosesAt;
            var due = start.AddHours(-1);
            if (due < DateTime.UtcNow) due = DateTime.UtcNow.AddMinutes(1);
            var availableVoters = votes.Where(v => v.SlotId == best.Id && v.Available).Select(v => v.UserId).Distinct();
            foreach (var uid in availableVoters)
            {
                try
                {
                    await _reminderRepository.AddAsync(new Reminder
                    {
                        DiscordUserId = uid,
                        GuildId = gn.GuildId,
                        ChannelId = gn.ChannelId,
                        Text = $"Game Night '{gn.Title ?? "Spieleabend"}' startet um {start:HH:mm} Uhr (Slot: {best.Label}).",
                        DueAt = due,
                        CreatedAt = DateTime.UtcNow
                    });
                }
                catch { }
            }
        }
    }
}
