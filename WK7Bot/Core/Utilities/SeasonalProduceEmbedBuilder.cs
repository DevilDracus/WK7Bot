namespace WK7Bot.Core.Utilities;

using Discord;
using WK7Bot.Models;

/// <summary>
/// Builds the seasonal produce calendar embed shared by the <c>/seasonal-produce</c> slash command and
/// the food publisher's monthly post, so both render the same layout with the same Discord limits.
/// </summary>
public static class SeasonalProduceEmbedBuilder
{
    /// <summary>
    /// Placeholder shown for a category the payload carries no entries for.
    /// </summary>
    public const string EmptyCategoryPlaceholder = "Keine angegeben";

    /// <summary>
    /// Renders the embed for one month.
    /// </summary>
    /// <param name="produce">The seasonal produce payload.</param>
    /// <param name="monthName">German month name used when the payload carries none.</param>
    /// <param name="footer">Footer text shown under the embed.</param>
    /// <returns>The rendered embed.</returns>
    public static Embed Build(SeasonalFoodData produce, string monthName, string footer)
    {
        ArgumentNullException.ThrowIfNull(produce);

        var month = string.IsNullOrWhiteSpace(produce.Month) ? monthName : produce.Month;

        return new EmbedBuilder()
            .WithTitle(EmbedText.Title($"🌱 Saisonkalender: {month}"))
            .WithDescription($"Übersicht der regionalen Saisonprodukte (Zentraleuropa / Leipzig-Region) für **{monthName}**.")
            .WithColor(Color.Green)
            .AddField("🍎 Obst", EmbedText.Field(JoinOrPlaceholder(produce.Fruits)), false)
            .AddField("🥕 Gemüse", EmbedText.Field(JoinOrPlaceholder(produce.Vegetables)), false)
            .AddField("🌿 Kräuter", EmbedText.Field(JoinOrPlaceholder(produce.Herbs)), false)
            .AddField("🌰 Nüsse", EmbedText.Field(JoinOrPlaceholder(produce.Nuts)), false)
            .WithFooter(footer)
            .WithCurrentTimestamp()
            .Build();
    }

    /// <summary>
    /// Joins the entries of a category, or reports that none were given.
    /// </summary>
    private static string JoinOrPlaceholder(System.Collections.Generic.List<string> items)
        => items.Count > 0 ? string.Join(", ", items) : EmptyCategoryPlaceholder;
}
