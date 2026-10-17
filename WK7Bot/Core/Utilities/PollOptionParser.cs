namespace WK7Bot.Core.Utilities;

/// <summary>
/// Parses the free-text answer list of the <c>/poll</c> command into Discord poll answers.
/// Answers are separated by a pipe (<c>|</c>) or a line break; blank entries are dropped.
/// </summary>
public static class PollOptionParser
{
    /// <summary>Discord renders at most ten poll answers.</summary>
    public const int MaxOptions = 10;

    /// <summary>Discord's per-answer character limit.</summary>
    public const int MaxOptionLength = 55;

    /// <summary>Discord's poll question character limit.</summary>
    public const int MaxQuestionLength = 300;

    /// <summary>Discord's poll duration ceiling in hours (one week).</summary>
    public const int MaxDurationHours = 168;

    /// <summary>
    /// Splits and normalizes the raw answer text.
    /// </summary>
    /// <param name="input">The raw answer list (pipe- or newline-separated).</param>
    /// <param name="error">A user-facing validation error, or <see langword="null"/> when parsing succeeded.</param>
    /// <returns>The parsed answers, each at most <see cref="MaxOptionLength"/> characters.</returns>
    public static IReadOnlyList<string> Parse(string? input, out string? error)
    {
        error = null;

        if (string.IsNullOrWhiteSpace(input))
        {
            error = $"Bitte gib mindestens 2 Antworten an, getrennt durch `|` oder Zeilenumbrüche (max. {MaxOptions}).";
            return Array.Empty<string>();
        }

        var answers = input
            .Split(new[] { '|', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Trim())
            .Where(part => part.Length > 0)
            .Select(part => part.Length > MaxOptionLength ? part[..MaxOptionLength] : part)
            .ToList();

        if (answers.Count < 2)
        {
            error = "Eine Umfrage braucht mindestens 2 Antworten.";
            return Array.Empty<string>();
        }

        if (answers.Count > MaxOptions)
        {
            error = $"Eine Umfrage erlaubt höchstens {MaxOptions} Antworten.";
            return Array.Empty<string>();
        }

        return answers;
    }

    /// <summary>
    /// Validates and normalizes the poll question.
    /// </summary>
    /// <param name="question">The raw question text.</param>
    /// <param name="error">A user-facing validation error, or <see langword="null"/> when valid.</param>
    /// <returns>The trimmed question.</returns>
    public static string ParseQuestion(string? question, out string? error)
    {
        error = null;
        var trimmed = (question ?? string.Empty).Trim();

        if (trimmed.Length == 0)
        {
            error = "Die Umfragefrage darf nicht leer sein.";
            return string.Empty;
        }

        if (trimmed.Length > MaxQuestionLength)
        {
            trimmed = trimmed[..MaxQuestionLength];
        }

        return trimmed;
    }
}
