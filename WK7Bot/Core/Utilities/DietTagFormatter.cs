namespace WK7Bot.Core.Utilities;

using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Maps the fixed English diet-tag identifiers returned by Gemini to German display labels for Discord embeds.
/// </summary>
public static class DietTagFormatter
{
    private static readonly Dictionary<string, string> GermanLabels = new(System.StringComparer.OrdinalIgnoreCase)
    {
        ["low_potassium"] = "Kaliumarm",
        ["low_phosphate"] = "Phosphatarm",
        ["low_sodium"] = "Natriumarm",
        ["low_carb"] = "Kohlenhydratarm",
        ["protein_rich"] = "Proteinreich",
        ["high_fiber"] = "Ballaststoffreich"
    };

    /// <summary>
    /// Resolves a single diet-tag identifier to its German display label, falling back to the raw tag when unknown.
    /// </summary>
    /// <param name="tag">The English diet-tag identifier (e.g., <c>low_potassium</c>).</param>
    /// <returns>The German label, or the input tag when no mapping exists.</returns>
    public static string ToGermanLabel(string tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
        {
            return string.Empty;
        }

        return GermanLabels.TryGetValue(tag, out var label) ? label : tag;
    }

    /// <summary>
    /// Formats a collection of diet-tag identifiers as German labels joined by a bullet separator.
    /// </summary>
    /// <param name="tags">The diet tags to format.</param>
    /// <returns>A bullet-separated label list, or <c>—</c> when no tags are present.</returns>
    public static string Format(IEnumerable<string>? tags)
    {
        if (tags == null)
        {
            return "—";
        }

        var labels = tags
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(ToGermanLabel)
            .ToList();

        return labels.Count == 0 ? "—" : string.Join(" • ", labels);
    }
}
