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
    /// <param name="hier">Post in the current channel instead of sending a direct message.</param>
    /// <returns>A task representing the command response operation.</returns>
    [SlashCommand("remind", "Legt eine Erinnerung an (per DM oder im Kanal).")]
    public async Task RemindAsync(
        [Summary("wann", "z. B. 'in 30m', 'in 2h', '18:30', 'morgen 08:00'")] string wann,
        [Summary("text", "Woran soll erinnert werden?")] [MaxLength(MaxReminderTextLength)] string text,
        [Summary("hier", "Im aktuellen Kanal erinnern statt per DM")] bool hier = false)
    {
        await DeferAsync(ephemeral: true);

        text = (text ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            await FollowupAsync("❌ Bitte gib an, woran erinnert werden soll.", ephemeral: true);
            return;
        }

        if (text.Length > MaxReminderTextLength)
        {
            text = text[..MaxReminderTextLength];
        }

        var now = DateTime.Now;
        if (!ReminderTimeParser.TryParse(wann, now, out var dueAt, out var error))
        {
            await FollowupAsync($"❌ {error}", ephemeral: true);
            return;
        }

        var reminder = new Reminder
        {
            DiscordUserId = Context.User.Id,
            GuildId = Context.Guild?.Id ?? 0,
            ChannelId = hier ? Context.Channel.Id : 0,
            Text = text,
            DueAt = dueAt.ToUniversalTime(),
            CreatedAt = DateTime.UtcNow
        };

        try
        {
            await _repository.AddAsync(reminder);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Could not store a reminder for user {UserId}.", Context.User.Id);
            await FollowupAsync("❌ Die Erinnerung konnte nicht gespeichert werden.", ephemeral: true);
            return;
        }

        var unix = new DateTimeOffset(dueAt).ToUnixTimeSeconds();
        var target = hier ? "in diesem Kanal" : "per DM";
        await FollowupAsync(
            $"⏰ Erinnerung {target} gesetzt für <t:{unix}:f> (<t:{unix}:R>):\n> {text}",
            ephemeral: true);
    }

    /// <summary>
    /// Lists the invoking user's pending reminders.
    /// </summary>
    /// <returns>A task representing the command response operation.</returns>
    [SlashCommand("reminders", "Zeigt deine offenen Erinnerungen.")]
    public async Task ListAsync()
    {
        await DeferAsync(ephemeral: true);

        var reminders = await _repository.GetPendingForUserAsync(Context.User.Id);
        if (reminders.Count == 0)
        {
            await FollowupAsync("Du hast keine offenen Erinnerungen. Nutze `/remind`.", ephemeral: true);
            return;
        }

        var builder = new StringBuilder();
        foreach (var reminder in reminders)
        {
            var unix = new DateTimeOffset(DateTime.SpecifyKind(reminder.DueAt, DateTimeKind.Utc)).ToUnixTimeSeconds();
            var target = reminder.ChannelId > 0 ? "Kanal" : "DM";
            builder.AppendLine($"`#{reminder.Id}` — <t:{unix}:f> (<t:{unix}:R>, {target}) — {reminder.Text}");
        }

        await FollowupAsync($"**Offene Erinnerungen**\n{builder.ToString().TrimEnd()}", ephemeral: true);
    }

    /// <summary>
    /// Cancels one of the invoking user's pending reminders.
    /// </summary>
    /// <param name="id">The reminder id shown by <c>/reminders</c>.</param>
    /// <returns>A task representing the command response operation.</returns>
    [SlashCommand("remind-cancel", "Löscht eine offene Erinnerung anhand ihrer ID.")]
    public async Task CancelAsync([Summary("id", "ID aus /reminders")] int id)
    {
        await DeferAsync(ephemeral: true);

        var removed = await _repository.RemovePendingAsync(id, Context.User.Id);
        await FollowupAsync(
            removed
                ? $"✅ Erinnerung `#{id}` gelöscht."
                : $"❌ Keine offene Erinnerung mit der ID `{id}` gefunden.",
            ephemeral: true);
    }
}
