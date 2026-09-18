namespace WK7Bot.Modules;

using Discord;
using Discord.Interactions;
using System.Globalization;
using WK7Bot.Services;

/// <summary>
/// Interaction module exposing slash commands for checking Leipzig waste collection schedules on demand.
/// </summary>
public class LeipzigWasteModule : InteractionModuleBase<SocketInteractionContext>
{
    private readonly ILeipzigWasteService _wasteService;

    /// <summary>
    /// Initializes a new instance of the <see cref="LeipzigWasteModule"/> class.
    /// </summary>
    /// <param name="wasteService">The waste calendar service instance.</param>
    public LeipzigWasteModule(ILeipzigWasteService wasteService)
    {
        _wasteService = wasteService;
    }

    /// <summary>
    /// Slash command handler executing real-time waste schedule lookups for a specific date or a relative range of days.
    /// </summary>
    /// <param name="days">Optional number of days ahead to check (e.g., 7 for the upcoming week).</param>
    /// <param name="dateInput">Optional specific date in DD.MM.YYYY or YYYY-MM-DD format.</param>
    /// <returns>A task representing the interaction response execution.</returns>
    [SlashCommand("check-waste", "Fragt die Müllabholtermine der Stadtreinigung Leipzig ab.")]
    public async Task CheckWasteAsync(
        [Summary("days", "Anzahl der Tage in der Zukunft, die abgefragt werden sollen (z. B. 7).")] int? days = null,
        [Summary("datum", "Spezifisches Datum (z. B. 20.09.2026 oder 2026-09-20).")] string? dateInput = null)
    {
        await DeferAsync(ephemeral: true);

        if (!string.IsNullOrWhiteSpace(dateInput))
        {
            await HandleSingleDateQueryAsync(dateInput);
            return;
        }

        var rangeDays = Math.Clamp(days ?? 2, 1, 14);
        await HandleDateRangeQueryAsync(rangeDays);
    }

    /// <summary>
    /// Parses a user-supplied date string and returns an ephemeral embed with collection details for that specific day.
    /// </summary>
    /// <param name="dateInput">Raw date string provided by the user in the interaction.</param>
    /// <returns>A task representing the asynchronous followup interaction response.</returns>
    private async Task HandleSingleDateQueryAsync(string dateInput)
    {
        string[] supportedFormats = ["dd.MM.yyyy", "yyyy-MM-dd", "d.M.yyyy"];

        if (!DateTime.TryParseExact(dateInput, supportedFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate))
        {
            await FollowupAsync("⚠️ Ungültiges Datumsformat. Bitte nutze **DD.MM.YYYY** (z. B. 20.09.2026) oder **YYYY-MM-DD**.", ephemeral: true);
            return;
        }

        var collections = await _wasteService.GetWasteTypesForDateAsync(parsedDate);
        var formattedList = collections.Count > 0
            ? string.Join("\n• ", collections)
            : "Keine Abholung an diesem Tag.";

        var embed = new EmbedBuilder()
            .WithTitle($"🗑️ Abholtermine für den {parsedDate:dd.MM.yyyy}")
            .WithDescription($"• {formattedList}")
            .WithColor(collections.Count > 0 ? Color.Green : Color.LightGrey)
            .WithCurrentTimestamp()
            .Build();

        await FollowupAsync(embed: embed, ephemeral: true);
    }

    /// <summary>
    /// Queries and constructs an ephemeral embed for a range of consecutive days starting from today.
    /// </summary>
    /// <param name="daysCount">The total number of consecutive days to process.</param>
    /// <returns>A task representing the asynchronous followup interaction response.</returns>
    private async Task HandleDateRangeQueryAsync(int daysCount)
    {
        var embedBuilder = new EmbedBuilder()
            .WithTitle($"🗑️ Stadtreinigung Leipzig — Vorschau ({daysCount} Tage)")
            .WithColor(Color.Blue)
            .WithCurrentTimestamp();

        var today = DateTime.Today;
        var foundAny = false;

        for (var i = 0; i < daysCount; i++)
        {
            var targetDate = today.AddDays(i);
            var collections = await _wasteService.GetWasteTypesForDateAsync(targetDate);

            if (collections.Count == 0 && daysCount > 3)
            {
                continue;
            }

            var dayLabel = i switch
            {
                0 => "Heute",
                1 => "Morgen",
                _ => targetDate.ToString("dddd", new CultureInfo("de-DE"))
            };

            var formattedText = collections.Count > 0
                ? string.Join("\n• ", collections)
                : "Keine Abholung.";

            embedBuilder.AddField($"{dayLabel} ({targetDate:dd.MM.yyyy})", formattedText, inline: false);

            if (collections.Count > 0)
            {
                foundAny = true;
            }
        }

        if (daysCount > 3 && !foundAny)
        {
            embedBuilder.WithDescription($"In den nächsten {daysCount} Tagen stehen keine Müllabholungen an.");
        }

        await FollowupAsync(embed: embedBuilder.Build(), ephemeral: true);
    }
}