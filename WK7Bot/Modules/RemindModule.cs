namespace WK7Bot.Modules;

using System.Text;
using Discord;
using Discord.Interactions;
using Microsoft.Extensions.Logging;
using WK7Bot.Core.Entities;
using WK7Bot.Core.Interfaces;
using WK7Bot.Core.Utilities;

/// <summary>
/// Personal reminders: <c>/remind</c> schedules one, <c>/reminders</c> lists the pending ones and
/// <c>/remind-cancel</c> removes one. Delivery is handled by <c>ReminderBackgroundService</c>.
/// </summary>
public class RemindModule : BotModuleBase
{
    /// <summary>Schema limit for the reminder text.</summary>
    private const int MaxReminderTextLength = 500;

    private readonly IReminderRepository _repository;
    private readonly ILogger<RemindModule> _logger;

    /// <summary>
    /// Gets the logger used for error reporting.
    /// </summary>
    protected override ILogger Logger => _logger;

    /// <summary>Initializes a new instance of the <see cref="RemindModule"/> class.</summary>
    /// <param name="repository">The reminder repository.</param>
    /// <param name="logger">The logger instance.</param>
    public RemindModule(IReminderRepository repository, ILogger<RemindModule> logger)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Schedules a reminder for the invoking user.
    /// </summary>
    /// <param name="wann">When to remind (for example <c>in 30m</c>, <c>18:30</c>, <c>morgen 08:00</c>).</param>
    /// <param name="text">What to remind about.</param>
    /// <param name="wiederholung">Optional recurrence, for example <c>daily</c>, <c>weekly</c>, <c>1d</c>, <c>2h</c>.</param>
    /// <param name="hier">Post in the current channel instead of sending a direct message.</param>
    /// <returns>A task representing the command response operation.</returns>
    [SlashCommand("remind", "Legt eine Erinnerung an (per DM oder im Kanal).")]
    public async Task RemindAsync(
        [Summary("wann", "z. B. 'in 30m', 'in 2h', '18:30', 'morgen 08:00'")] string wann,
        [Summary("text", "Woran soll erinnert werden?")][MaxLength(MaxReminderTextLength)] string text,
        [Summary("wiederholung", "Wiederholung, z. B. 'daily', 'weekly', '1d', '2h' (optional)")] string? wiederholung = null,
        [Summary("hier", "Im aktuellen Kanal erinnern statt per DM")] bool hier = false)
    {
        await DeferAsync(ephemeral: true).ConfigureAwait(false);

        var (reminderText, dueAt, validationError) = await ParseAndValidateReminderAsync(text, wann, wiederholung).ConfigureAwait(false);

        if (validationError is not null)
        {
            await FollowupAsync($"❌ {validationError}", ephemeral: true).ConfigureAwait(false);
            return;
        }

        var reminder = CreateReminderEntity(Context.User.Id, Context.Guild, Context.Channel, reminderText, dueAt, wiederholung);

        if (!await StoreReminderAsync(reminder).ConfigureAwait(false))
        {
            return;
        }

        await SendReminderConfirmationAsync(reminder, hier).ConfigureAwait(false);
    }

    private static async Task<(string ReminderText, DateTimeOffset DueAt, string? ValidationError)> ParseAndValidateReminderAsync(
        string text,
        string wann,
        string? wiederholung)
    {
        text = (text ?? string.Empty).Trim();

        if (text.Length == 0)
        {
            return (string.Empty, DateTimeOffset.UtcNow, "Bitte gib an, woran erinnert werden soll.");
        }

        if (text.Length > MaxReminderTextLength)
        {
            text = text[..MaxReminderTextLength];
        }

        var now = DateTime.UtcNow;
        if (!ReminderTimeParser.TryParse(wann, now, out var dueAt, out var error))
        {
            return (text, DateTimeOffset.UtcNow, error);
        }

        var (isRecurring, recurrenceTicks, recurrenceError) = await ParseRecurrenceAsync(wiederholung).ConfigureAwait(false);

        if (recurrenceError is not null)
        {
            return (text, dueAt, recurrenceError);
        }

        return (text, dueAt, null);
    }

    private static async Task<(bool IsRecurring, long RecurrenceTicks, string? Error)> ParseRecurrenceAsync(string? wiederholung)
    {
        if (string.IsNullOrWhiteSpace(wiederholung))
        {
            return (false, 0L, null);
        }

        if (!ReminderTimeParser.TryParseRecurrence(wiederholung, out var interval))
        {
            return (false, 0L, "Die Wiederholung konnte nicht gelesen werden. Beispiele: `daily`, `weekly`, `1d`, `2h`.");
        }

        return (true, interval.Ticks, null);
    }

    private static Reminder CreateReminderEntity(
        ulong userId,
        IGuild? guild,
        IChannel? channel,
        string reminderText,
        DateTimeOffset dueAt,
        string? wiederholung)
    {
        var isRecurring = !string.IsNullOrWhiteSpace(wiederholung);
        var recurrenceTicks = isRecurring ? ReminderTimeParser.TryParseRecurrence(wiederholung, out var interval) == true ? interval.Ticks : 0L : 0L;

        return new Reminder
        {
            DiscordUserId = userId,
            GuildId = guild?.Id ?? 0,
            ChannelId = channel?.Id ?? 0,
            Text = reminderText,
            DueAt = dueAt.UtcDateTime,
            CreatedAt = DateTime.UtcNow,
            IsRecurring = isRecurring,
            RecurrenceTicks = recurrenceTicks
        };
    }

    /// <summary>Persists a reminder and returns whether it was stored.</summary>
    /// <param name="reminder">The reminder to store.</param>
    /// <returns>True when stored.</returns>
    private async Task<bool> StoreReminderAsync(Reminder reminder)
    {
        try
        {
            await _repository.AddAsync(reminder).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || ex is TaskCanceledException)
        {
            _logger.LogError(ex, "Could not store a reminder for user {UserId}.", Context.User.Id);
            await FollowupAsync("❌ Die Erinnerung konnte nicht gespeichert werden.", ephemeral: true).ConfigureAwait(false);
            return false;
        }
    }

    /// <summary>Sends a confirmation message for a stored reminder.</summary>
    /// <param name="reminder">The reminder.</param>
    /// <param name="hier">Whether to post in the current channel.</param>
    /// <returns>A task tracking the operation.</returns>
    private async Task SendReminderConfirmationAsync(Reminder reminder, bool hier)
    {
        var unix = new DateTimeOffset(reminder.DueAt, TimeSpan.Zero).ToUnixTimeSeconds();
        var target = hier ? "in diesem Kanal" : "per DM";
        var recurringNote = reminder.IsRecurring ? " (wiederholt sich)" : string.Empty;

        await FollowupAsync(
            $"⏰ Erinnerung {target} gesetzt für <t:{unix}:f> (<t:{unix}:R>){recurringNote}:\n> {reminder.Text}",
            ephemeral: true).ConfigureAwait(false);
    }

    /// <summary>
    /// Lists the invoking user's pending reminders.
    /// </summary>
    [SlashCommand("reminders", "Zeigt deine offenen Erinnerungen an.")]
    public async Task ListAsync()
    {
        await DeferAsync(ephemeral: true).ConfigureAwait(false);

        var reminders = await _repository.GetPendingForUserAsync(Context.User.Id);
        if (reminders.Count == 0)
        {
            await FollowupAsync("Du hast keine offenen Erinnerungen. Nutze `/remind`.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        var builder = new StringBuilder();
        foreach (var reminder in reminders)
        {
            var unix = new DateTimeOffset(DateTime.SpecifyKind(reminder.DueAt, DateTimeKind.Utc)).ToUnixTimeSeconds();
            var target = reminder.ChannelId > 0 ? "Kanal" : "DM";
            builder.AppendLine($"`#{reminder.Id}` — <t:{unix}:f> (<t:{unix}:R>, {target}) — {reminder.Text}");
        }

        await FollowupAsync($"**Offene Erinnerungen**\n{builder.ToString().TrimEnd()}", ephemeral: true).ConfigureAwait(false);
    }

    /// <summary>
    /// Cancels one of the invoking user's pending reminders.
    /// </summary>
    /// <param name="id">The reminder id shown by <c>/reminders</c>.</param>
    /// <returns>A task representing the command response operation.</returns>
    [SlashCommand("remind-cancel", "Löscht eine offene Erinnerung anhand ihrer ID.")]
    public async Task CancelAsync([Summary("id", "ID aus /reminders")] int id)
    {
        await DeferAsync(ephemeral: true).ConfigureAwait(false);

        var removed = await _repository.RemovePendingAsync(id, Context.User.Id);
        await FollowupAsync(
            removed
                ? $"✅ Erinnerung `#{id}` gelöscht."
                : $"❌ Keine offene Erinnerung mit der ID `{id}` gefunden.",
            ephemeral: true);
    }
}
