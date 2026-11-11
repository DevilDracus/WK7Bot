namespace WK7Bot.Modules;

using System.Text.Json;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;
using WK7Bot.Core.Interfaces;
using WK7Bot.Core.Utilities;

/// <summary>
/// Trivia: <c>/quiz</c> starts a round with a random question and answer buttons.
/// </summary>
public class TriviaModule : BotModuleBase
{
    private static readonly Random Rng = new();
    private static readonly Dictionary<ulong, int> ActiveAnswers = new(); // messageId -> correct option index

    private readonly ITriviaScoreRepository _scoreRepository;
    private readonly ILogger<TriviaModule> _logger;

    /// <summary>Gets the logger used for error reporting.</summary>
    protected override ILogger Logger => _logger;

    /// <summary>Initializes a new instance of the <see cref="TriviaModule"/> class.</summary>
    /// <param name="scoreRepository">The trivia score repository.</param>
    /// <param name="logger">The logger instance.</param>
    public TriviaModule(ITriviaScoreRepository scoreRepository, ILogger<TriviaModule> logger)
    {
        _scoreRepository = scoreRepository ?? throw new ArgumentNullException(nameof(scoreRepository));
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
            try
            {
                var weekStart = GetWeekStart(DateTime.UtcNow);
                await _scoreRepository.IncrementAsync(Context.User.Id, Context.Guild?.Id ?? 0, weekStart, 1).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not persist trivia score for user {UserId}.", Context.User.Id);
            }

            await RespondAsync($"✅ Richtig, <@{Context.User.Id}>!", ephemeral: false).ConfigureAwait(false);
            lock (ActiveAnswers) { if (component != null) ActiveAnswers.Remove(component.Message.Id); }
        }
        else
        {
            await RespondAsync("❌ Leider falsch. Versuch's nochmal!", ephemeral: true).ConfigureAwait(false);
        }
    }

    /// <summary>Shows the trivia leaderboard for the current week.</summary>
    /// <returns>A task representing the command response.</returns>
    [SlashCommand("trivia-leaderboard", "Zeigt das Trivia-Leaderboard der aktuellen Woche.")]
    public async Task LeaderboardAsync()
    {
        await DeferAsync(ephemeral: true).ConfigureAwait(false);

        var guildId = Context.Guild?.Id ?? 0;
        var weekStart = GetWeekStart(DateTime.UtcNow);
        var top = await _scoreRepository.GetTopAsync(guildId, weekStart, 10).ConfigureAwait(false);

        if (top.Count == 0)
        {
            await FollowupAsync("Noch keine Punkte diese Woche. Starte mit `/quiz`!", ephemeral: true).ConfigureAwait(false);
            return;
        }

        var builder = new System.Text.StringBuilder();
        builder.AppendLine("**🏆 Trivia-Leaderboard (diese Woche)**");
        var rank = 1;
        foreach (var entry in top)
        {
            builder.AppendLine($"{rank}. <@{entry.DiscordUserId}> — {entry.Score} Punkt{(entry.Score == 1 ? "" : "e")}");
            rank++;
        }

        await FollowupAsync(builder.ToString().TrimEnd(), ephemeral: true).ConfigureAwait(false);
    }

    private static DateTime GetWeekStart(DateTime utc)
    {
        var date = utc.Date;
        var diff = (7 + (date.DayOfWeek - DayOfWeek.Monday)) % 7;
        return date.AddDays(-diff);
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
