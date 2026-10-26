namespace WK7Bot.Modules;

using Discord;
using Discord.Interactions;
using Microsoft.Extensions.Logging;
using WK7Bot.Core.Utilities;

/// <summary>
/// Ad-hoc poll command. Uses Discord's native poll feature so votes need no bot-side bookkeeping.
/// </summary>
public class PollModule : BotModuleBase
{
    private readonly ILogger<PollModule> _logger;

    /// <summary>
    /// Gets the logger used for error reporting.
    /// </summary>
    protected override ILogger Logger => _logger;

    /// <summary>Initializes a new instance of the <see cref="PollModule"/> class.</summary>
    /// <param name="logger">The logger instance.</param>
    public PollModule(ILogger<PollModule> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Posts a native Discord poll.
    /// </summary>
    /// <param name="frage">The poll question.</param>
    /// <param name="optionen">The answers, separated by <c>|</c> or line breaks (2-10).</param>
    /// <param name="dauer_stunden">The poll duration in hours (1-168).</param>
    /// <param name="mehrfachauswahl">Whether voters may pick more than one answer.</param>
    /// <returns>A task representing the command response operation.</returns>
    [SlashCommand("poll", "Erstellt eine Umfrage mit nativen Discord-Abstimmungen.")]
    public async Task PollAsync(
        [Summary("frage", "Die Umfragefrage")] [MaxLength(PollOptionParser.MaxQuestionLength)] string frage,
        [Summary("optionen", "Antworten, getrennt durch | oder Zeilenumbrüche (2-10)")] string optionen,
        [Summary("dauer_stunden", "Laufzeit in Stunden (1-168)")] int dauer_stunden = 24,
        [Summary("mehrfachauswahl", "Mehrere Antworten erlauben")] bool mehrfachauswahl = false)
    {
        await DeferAsync().ConfigureAwait(false);

        var question = PollOptionParser.ParseQuestion(frage, out var questionError);
        if (questionError is not null)
        {
            await FollowupAsync($"❌ {questionError}", ephemeral: true).ConfigureAwait(false);
            return;
        }

        var answers = PollOptionParser.Parse(optionen, out var optionsError);
        if (optionsError is not null)
        {
            await FollowupAsync($"❌ {optionsError}", ephemeral: true).ConfigureAwait(false);
            return;
        }

        var duration = (uint)Math.Clamp(dauer_stunden, 1, PollOptionParser.MaxDurationHours);

        var poll = new PollProperties
        {
            Question = new PollMediaProperties { Text = question },
            Answers = answers.Select(answer => new PollMediaProperties { Text = answer }).ToList(),
            Duration = duration,
            AllowMultiselect = mehrfachauswahl,

            // PollLayout has no zero value; the uninitialised default serialises as layout_type 0,
            // which Discord rejects with 50035 (only layout type 1 is accepted).
            LayoutType = PollLayout.Default
        };

        await FollowupAsync(poll: poll).ConfigureAwait(false);
    }
}
