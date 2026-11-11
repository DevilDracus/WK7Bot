namespace WK7Bot.Modules;

using System.Text.Json;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;
using WK7Bot.Core.Utilities;

/// <summary>
/// Trivia: <c>/quiz</c> starts a round with a random question and answer buttons.
/// </summary>
public class TriviaModule : BotModuleBase
{
    private static readonly Random Rng = new();
    private static readonly Dictionary<ulong, int> ActiveAnswers = new(); // messageId -> correct option index

    private readonly ILogger<TriviaModule> _logger;

    /// <summary>Gets the logger used for error reporting.</summary>
    protected override ILogger Logger => _logger;

    /// <summary>Initializes a new instance of the <see cref="TriviaModule"/> class.</summary>
    /// <param name="logger">The logger instance.</param>
    public TriviaModule(ILogger<TriviaModule> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>Starts a trivia round with a random question.</summary>
    /// <returns>A task representing the command response.</returns>
    [SlashCommand("quiz", "Startet eine Trivia-Runde mit einer Zufallsfrage.")]
    public async Task QuizAsync()
    {
        await DeferAsync().ConfigureAwait(false);

        var questions = LoadQuestions();
        if (questions.Count == 0)
        {
            await FollowupAsync("❌ Keine Fragen verfügbar.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        var q = questions[Rng.Next(questions.Count)];
        var builder = new ComponentBuilder();
        for (var i = 0; i < q.Options.Count; i++)
        {
            builder.WithButton(q.Options[i], $"trivia:answer:{i}", ButtonStyle.Secondary, row: i / 2);
        }

        var message = await FollowupAsync(
            $"❓ **{q.Question}**",
            components: builder.Build()).ConfigureAwait(false);

        lock (ActiveAnswers)
        {
            ActiveAnswers[message.Id] = q.Answer;
        }
    }

    /// <summary>Handles an answer button click.</summary>
    /// <param name="optionIndex">The selected option index.</param>
    /// <returns>A task representing the interaction response.</returns>
    [ComponentInteraction("trivia:answer:*")]
    public async Task AnswerAsync(int optionIndex)
    {
        var component = Context.Interaction as SocketMessageComponent;
        if (component is null || !ActiveAnswers.TryGetValue(component.Message.Id, out var correct))
        {
            await RespondAsync("Diese Runde ist nicht mehr aktiv.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        if (optionIndex == correct)
        {
            await RespondAsync($"✅ Richtig, <@{Context.User.Id}>!", ephemeral: false).ConfigureAwait(false);
            lock (ActiveAnswers) { if (component != null) ActiveAnswers.Remove(component.Message.Id); }
        }
        else
        {
            await RespondAsync("❌ Leider falsch. Versuch's nochmal!", ephemeral: true).ConfigureAwait(false);
        }
    }

    private static List<TriviaQuestion> LoadQuestions()
    {
        try
        {
            var assembly = typeof(TriviaModule).Assembly;
            var resourceName = assembly.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith("trivia-questions.json", StringComparison.OrdinalIgnoreCase));
            if (resourceName is null)
            {
                return new List<TriviaQuestion>();
            }

            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream is null)
            {
                return new List<TriviaQuestion>();
            }

            using var reader = new StreamReader(stream);
            var json = reader.ReadToEnd();
            return JsonSerializer.Deserialize<List<TriviaQuestion>>(json) ?? new List<TriviaQuestion>();
        }
        catch
        {
            return new List<TriviaQuestion>();
        }
    }

    private sealed class TriviaQuestion
    {
        public string Question { get; set; } = string.Empty;
        public List<string> Options { get; set; } = new();
        public int Answer { get; set; }
    }
}
