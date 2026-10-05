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

        if (ContainsAny(rawSummary, "biogut", "bio ", "braun"))
        {
            return "🟫 Braune Tonne (Biogut)";
        }

        return $"🗑️ {rawSummary}";
    }

    private static bool ContainsAny(string haystack, params string[] needles)
        => needles.Any(n => haystack.Contains(n, StringComparison.OrdinalIgnoreCase));
}
