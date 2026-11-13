namespace WK7Bot.Modules;

using System.Globalization;
using Discord;
using Discord.Interactions;
using Microsoft.Extensions.Logging;
using WK7Bot.Core.Entities;
using WK7Bot.Core.Interfaces;
using WK7Bot.Core.Utilities;

/// <summary>
/// Birthday tracking: <c>/birthday set</c>, <c>/birthday remove</c>, <c>/birthday show</c>.
/// Announcements are delivered by <c>BirthdayBackgroundService</c>.
/// </summary>
public class BirthdayModule : BotModuleBase
{
    private readonly IBirthdayRepository _repository;
    private readonly ILogger<BirthdayModule> _logger;

    /// <summary>Gets the logger used for error reporting.</summary>
    protected override ILogger Logger => _logger;

    /// <summary>Initializes a new instance of the <see cref="BirthdayModule"/> class.</summary>
    /// <param name="repository">The birthday repository.</param>
    /// <param name="logger">The logger instance.</param>
    public BirthdayModule(IBirthdayRepository repository, ILogger<BirthdayModule> logger)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>Sets or updates the invoking user's birthday.</summary>
    /// <param name="day">Day of month (1-31).</param>
    /// <param name="month">Month (1-12).</param>
    /// <returns>A task representing the command response.</returns>
    [SlashCommand("birthday-set", "Setzt deinen Geburtstag (Tag und Monat).")]
    public async Task SetAsync(
        [Summary("tag", "Tag des Monats (1-31)")] int day,
        [Summary("monat", "Monat (1-12)")] int month)
        => await SetBirthdayAsync(day, month, "birthday-set").ConfigureAwait(false);

    /// <summary>Adds or updates the invoking user's birthday (alias for <c>/birthday-set</c>).</summary>
    /// <param name="day">Day of month (1-31).</param>
    /// <param name="month">Month (1-12).</param>
    /// <returns>A task representing the command response.</returns>
    [SlashCommand("add-birthday", "Fügt deinen Geburtstag hinzu (Tag und Monat).")]
    public async Task AddBirthdayAsync(
        [Summary("tag", "Tag des Monats (1-31)")] int day,
        [Summary("monat", "Monat (1-12)")] int month)
        => await SetBirthdayAsync(day, month, "add-birthday").ConfigureAwait(false);

    /// <summary>Sets the invoking user's birthday.</summary>
    /// <param name="day">The day of month.</param>
    /// <param name="month">The month.</param>
    /// <param name="command">The slash-command name reported by the test-mode dry run.</param>
    /// <returns>A task tracking the operation.</returns>
    private async Task SetBirthdayAsync(int day, int month, string command)
    {
        await DeferAsync(ephemeral: true).ConfigureAwait(false);

        if (month < 1 || month > 12 || day < 1 || day > DateTime.DaysInMonth(2000, month))
        {
            await FollowupAsync("❌ Ungültiges Datum.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        if (TestMode.IsActive)
        {
            await FollowupAsync(TestMode.Simulated(command), ephemeral: true).ConfigureAwait(false);
            return;
        }

        var birthday = new Birthday
        {
            DiscordUserId = Context.User.Id,
            GuildId = Context.Guild?.Id ?? 0,
            Day = day,
            Month = month
        };

        try
        {
            await _repository.UpsertAsync(birthday).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || ex is TaskCanceledException)
        {
            _logger.LogError(ex, "Could not store birthday for user {UserId}.", Context.User.Id);
            await FollowupAsync("❌ Der Geburtstag konnte nicht gespeichert werden.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        await FollowupAsync($"🎂 Geburtstag gesetzt: {day:00}.{month:00}.", ephemeral: true).ConfigureAwait(false);
    }

    /// <summary>Shows the invoking user's stored birthday.</summary>
    /// <returns>A task representing the command response.</returns>
    [SlashCommand("birthday-show", "Zeigt deinen gespeicherten Geburtstag.")]
    public async Task ShowAsync()
    {
        await DeferAsync(ephemeral: true).ConfigureAwait(false);

        var birthday = await _repository.GetForUserAsync(Context.User.Id);
        if (birthday is null)
        {
            await FollowupAsync("Du hast noch keinen Geburtstag gespeichert. Nutze `/birthday-set`.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        await FollowupAsync($"🎂 Dein Geburtstag: {birthday.Day:00}.{birthday.Month:00}.", ephemeral: true).ConfigureAwait(false);
    }

    /// <summary>Removes the invoking user's stored birthday.</summary>
    /// <returns>A task representing the command response.</returns>
    [SlashCommand("birthday-remove", "Entfernt deinen gespeicherten Geburtstag.")]
    public async Task RemoveAsync()
    {
        await DeferAsync(ephemeral: true).ConfigureAwait(false);

        if (TestMode.IsActive)
        {
            await FollowupAsync(TestMode.Simulated("birthday-remove"), ephemeral: true).ConfigureAwait(false);
            return;
        }

        var removed = await _repository.RemoveAsync(Context.User.Id);
        await FollowupAsync(
            removed ? "✅ Geburtstag entfernt." : "❌ Kein Geburtstag gespeichert.",
            ephemeral: true).ConfigureAwait(false);
    }
}
