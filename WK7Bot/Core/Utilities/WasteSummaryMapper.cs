namespace WK7Bot.Core.Utilities;

/// <summary>
/// Maps raw ICS event summaries from Stadtreinigung Leipzig into user-friendly German display strings.
/// </summary>
public static class WasteSummaryMapper
{
    /// <summary>
    /// Maps a raw calendar event summary to a localized, emoji-prefixed display label.
    /// </summary>
    /// <param name="rawSummary">The raw SUMMARY text from the ICS event (may be empty).</param>
    /// <returns>A formatted German display string with emoji.</returns>
    public static string MapToFriendlyName(string rawSummary)
    {
        if (string.IsNullOrWhiteSpace(rawSummary))
        {
            return "🗑️ Unbekannte Abfuhr";
        }

        // Precedence: specific waste types before generic color keywords to reduce misclassification.
        if (ContainsAny(rawSummary, "restabfall", "schwarz"))
        {
            return "⬛ Schwarze Tonne (Restabfall)";
        }

        if (ContainsAny(rawSummary, "wertstoff", "gelb"))
        {
            return "🟨 Gelbe Tonne / Gelber Sack (Wertstoffe)";
        }

        if (ContainsAny(rawSummary, "papier", "pappe", "blau"))
        {
            return "🟦 Blaue Tonne (Pappe & Papier)";
        }

        if (ContainsBioKeyword(rawSummary))
        {
            return "🟫 Braune Tonne (Biogut)";
        }

        return $"🗑️ {rawSummary}";
    }

    private static bool ContainsAny(string haystack, params string[] needles)
        => needles.Any(n => haystack.Contains(n, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Matches the brown-bin waste keywords: the compound terms are matched as substrings, while the
    /// bare word "bio" requires letter boundaries so that unrelated words containing it stay untouched.
    /// </summary>
    /// <param name="summary">The raw ICS event summary.</param>
    /// <returns><see langword="true"/> when the summary refers to bio waste.</returns>
    private static bool ContainsBioKeyword(string summary)
        => ContainsAny(summary, "biogut", "bioabfall", "biomüll", "braun")
           || ContainsStandaloneWord(summary, "bio");

    /// <summary>
    /// Determines whether a word occurs in the haystack without being embedded inside a longer
    /// letter-run (e.g. "bio" matches "Bio," or "und Bio" but not "Biografie").
    /// </summary>
    /// <param name="haystack">The text to search.</param>
    /// <param name="word">The word to look for (case-insensitive).</param>
    /// <returns><see langword="true"/> when the word occurs standalone.</returns>
    private static bool ContainsStandaloneWord(string haystack, string word)
    {
        var index = haystack.IndexOf(word, StringComparison.OrdinalIgnoreCase);

        while (index >= 0)
        {
            var startIsBoundary = index == 0 || !char.IsLetter(haystack[index - 1]);
            var end = index + word.Length;
            var endIsBoundary = end >= haystack.Length || !char.IsLetter(haystack[end]);

            if (startIsBoundary && endIsBoundary)
            {
                return true;
            }

            index = haystack.IndexOf(word, index + 1, StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }
}
