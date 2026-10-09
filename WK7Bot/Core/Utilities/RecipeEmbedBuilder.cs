namespace WK7Bot.Core.Utilities;

using System;
using System.Collections.Generic;
using System.Linq;
using Discord;
using WK7Bot.Models;

/// <summary>
/// Builds the Discord recipe embeds shared by the <c>/recipe</c> commands and the scheduled food publisher, keeping
/// field limits, optional diet/safety sections, and the source disclaimer of web-searched recipes in one place.
/// </summary>
public static class RecipeEmbedBuilder
{
    /// <summary>
    /// Discord's total embed length limit (title + description + footer + all field names and values).
    /// </summary>
    private const int EmbedLengthLimit = 6000;

    /// <summary>
    /// Head-room kept below <see cref="EmbedLengthLimit"/> so the embed is never rejected by Discord.
    /// </summary>
    private const int EmbedLengthSafety = 150;

    private const string OverflowHint =
        "… zu lang für Discord – das vollständige Rezept steht hinter dem Link am Titel bzw. in der Quelle.";

    private const string DietTagsFieldName = "🏷️ Diet Tags";
    private const string SafetyFieldName = "🛡️ Safety & Renal Notes";
    private const string SeasonalFieldName = "🌿 Saisonale Zutaten";
    private const string DisclaimerFieldName = "⚠️ Hinweis";

    /// <summary>
    /// Builds the embed for a Gemini generated, renal screened recipe.
    /// </summary>
    /// <param name="recipe">The generated recipe payload.</param>
    /// <param name="footer">The footer text shown under the embed.</param>
    /// <returns>The resulting embed.</returns>
    public static Embed BuildGenerated(RenalRecipeData recipe, string footer)
        => Build(recipe, $"🥗 Recipe of the Week: {recipe.Title}", Color.Teal, footer, includeDisclaimer: false);

    /// <summary>
    /// Builds the embed for a recipe parsed from the web, linking back to its source page and carrying a disclaimer
    /// that the recipe was not screened for dialysis or transplant safety.
    /// </summary>
    /// <param name="recipe">The searched recipe payload; must expose <see cref="RenalRecipeData.SourceUrl"/>.</param>
    /// <param name="footer">The footer text shown under the embed.</param>
    /// <returns>The resulting embed.</returns>
    public static Embed BuildSearched(RenalRecipeData recipe, string footer)
        => Build(recipe, $"🍽️ {recipe.Title}", Color.DarkOrange, footer, includeDisclaimer: true);

    /// <summary>
    /// Shared embed assembly for both recipe sources. Long sections (ingredients, steps) are split across several
    /// fields so no content is ever dropped; the remaining budget is capped so the embed stays inside Discord's
    /// total length limit.
    /// </summary>
    private static Embed Build(RenalRecipeData recipe, string title, Color color, string footer, bool includeDisclaimer)
    {
        ArgumentNullException.ThrowIfNull(recipe);

        // Hard Discord limits for the fixed slots; recipe lists are chunked instead of truncated.
        var safeTitle = EmbedText.Truncate(title, EmbedText.TitleLimit);
        var safeFooter = EmbedText.Truncate(footer ?? string.Empty, EmbedText.FooterLimit);

        // Sections that are added after the variable-length ones are measured up front (field names
        // included) so the recipe text can never crowd out the diet/safety/disclaimer fields and the
        // embed stays inside Discord's total length limit.
        var dietTags = recipe.DietTags ?? new List<string>();
        var formattedTags = dietTags.Count > 0 ? DietTagFormatter.Format(dietTags) : string.Empty;
        var safetyNotes = string.IsNullOrWhiteSpace(recipe.TransplantSafetyNotes)
            ? string.Empty
            : recipe.TransplantSafetyNotes!;
        var seasonalIngredients = string.Join(", ", recipe.SeasonalIngredientsUsed ?? new List<string>());
        var disclaimer = includeDisclaimer ? BuildDisclaimer(recipe.SourceUrl) : string.Empty;

        int reserved =
            (formattedTags.Length > 0 ? DietTagsFieldName.Length + formattedTags.Length : 0)
            + (safetyNotes.Length > 0 ? SafetyFieldName.Length + safetyNotes.Length : 0)
            + (seasonalIngredients.Length > 0 ? SeasonalFieldName.Length + seasonalIngredients.Length : 0)
            + (disclaimer.Length > 0 ? DisclaimerFieldName.Length + disclaimer.Length : 0);

        int budget = EmbedLengthLimit - EmbedLengthSafety
            - safeTitle.Length
            - safeFooter.Length
            - reserved;

        var description = EmbedText.Truncate(BuildDescription(recipe), Math.Clamp(budget, 0, EmbedText.DescriptionLimit));
        budget -= description.Length;

        var builder = new EmbedBuilder()
            .WithTitle(safeTitle)
            .WithDescription(description)
            .WithColor(color);

        var ingredients = FormatIngredients(recipe.Ingredients);
        if (ingredients.Length > 0)
        {
            budget -= AddChunkedField(builder, "🛒 Ingredients", ingredients, budget);
        }

        var instructions = FormatInstructions(recipe.Instructions);
        if (instructions.Length > 0)
        {
            budget -= AddChunkedField(builder, "👨‍🍳 Preparation Steps", instructions, budget);
        }

        if (formattedTags.Length > 0)
        {
            budget = AddFixedField(builder, DietTagsFieldName, formattedTags, budget);
        }

        if (safetyNotes.Length > 0)
        {
            budget = AddFixedField(builder, SafetyFieldName, safetyNotes, budget);
        }

        if (seasonalIngredients.Length > 0)
        {
            budget = AddFixedField(builder, SeasonalFieldName, seasonalIngredients, budget);
        }

        if (includeDisclaimer)
        {
            if (Uri.TryCreate(recipe.SourceUrl, UriKind.Absolute, out var sourceUri))
            {
                builder.WithUrl(sourceUri.ToString());
            }

            if (!string.IsNullOrWhiteSpace(recipe.ImageUrl))
            {
                builder.WithThumbnailUrl(recipe.ImageUrl);
            }

            if (disclaimer.Length > 0)
            {
                AddFixedField(builder, DisclaimerFieldName, disclaimer, budget);
            }
        }

        if (!string.IsNullOrWhiteSpace(safeFooter))
        {
            builder.WithFooter(safeFooter);
        }

        builder.WithCurrentTimestamp();
        return builder.Build();
    }

    /// <summary>
    /// Adds a fixed single-field section clamped to the remaining embed-wide budget (field name included),
    /// skipping it entirely when not even the name fits.
    /// </summary>
    /// <returns>The remaining budget after the section was added or skipped.</returns>
    private static int AddFixedField(EmbedBuilder builder, string name, string value, int budget)
    {
        if (string.IsNullOrEmpty(value) || budget <= 0)
        {
            return budget;
        }

        if (name.Length + value.Length <= budget)
        {
            builder.AddField(name, value, false);
            return budget - name.Length - value.Length;
        }

        int room = budget - name.Length;
        if (room < 16)
        {
            return budget;
        }

        builder.AddField(name, EmbedText.Truncate(value, room), false);
        return 0;
    }

    /// <summary>
    /// Adds a field value, split across as many fields as needed to stay within Discord's per-field limit, and stops
    /// with a pointer to the source once the embed-wide budget is exhausted.
    /// </summary>
    /// <returns>The number of characters consumed from <paramref name="budget"/> (field names included).</returns>
    private static int AddChunkedField(EmbedBuilder builder, string name, string value, int budget)
    {
        if (string.IsNullOrEmpty(value))
        {
            return 0;
        }

        var chunks = SplitValue(value, EmbedText.FieldValueLimit);
        if (chunks.Count == 0)
        {
            return 0;
        }

        if (chunks.Count == 1)
        {
            builder.AddField(name, chunks[0], false);
            return name.Length + chunks[0].Length;
        }

        int consumed = 0;
        for (int i = 0; i < chunks.Count; i++)
        {
            string fieldName = $"{name} ({i + 1}/{chunks.Count})";
            int cost = fieldName.Length + chunks[i].Length;
            if (cost > budget - consumed)
            {
                string hintName = "⚠️ Rezept gekürzt";
                int hintCost = hintName.Length + OverflowHint.Length;
                if (hintCost <= budget - consumed)
                {
                    builder.AddField(hintName, OverflowHint, false);
                    consumed += hintCost;
                }

                return consumed;
            }

            builder.AddField(fieldName, chunks[i], false);
            consumed += cost;
        }

        return consumed;
    }

    /// <summary>
    /// Splits a field value into chunks of at most <paramref name="limit"/> characters, preferring line breaks so
    /// numbered steps and ingredient lines stay intact.
    /// </summary>
    private static IReadOnlyList<string> SplitValue(string value, int limit)
    {
        var chunks = new List<string>();
        var current = new List<string>();
        int currentLength = 0;

        void Flush()
        {
            if (current.Count == 0)
            {
                return;
            }

            chunks.Add(string.Join("\n", current));
            current.Clear();
            currentLength = 0;
        }

        foreach (var rawLine in value.Split('\n'))
        {
            var line = rawLine;
            while (line.Length > limit)
            {
                Flush();
                chunks.Add(line[..limit]);
                line = line[limit..];
            }

            int added = line.Length + (current.Count > 0 ? 1 : 0);
            if (currentLength + added > limit)
            {
                Flush();
                added = line.Length;
            }

            current.Add(line);
            currentLength += added;
        }

        Flush();
        return chunks;
    }

    /// <summary>
    /// Composes the embed description including whichever timing and serving details are available.
    /// </summary>
    private static string BuildDescription(RenalRecipeData recipe)
    {
        var meta = new List<string>();
        if (!string.IsNullOrWhiteSpace(recipe.PrepTime))
        {
            meta.Add($"⏱️ **Prep Time:** {recipe.PrepTime}");
        }

        if (!string.IsNullOrWhiteSpace(recipe.CookTime))
        {
            meta.Add($"🍳 **Cook Time:** {recipe.CookTime}");
        }

        if (recipe.Servings > 0)
        {
            meta.Add($"🍽️ **Servings:** {recipe.Servings}");
        }

        var description = string.IsNullOrWhiteSpace(recipe.Description) ? string.Empty : recipe.Description;
        if (meta.Count == 0)
        {
            return description;
        }

        var metaLine = string.Join(" | ", meta);
        return description.Length == 0 ? metaLine : $"{description}\n\n{metaLine}";
    }

    /// <summary>
    /// Builds the disclaimer shown for recipes that originate from an unverified web source.
    /// </summary>
    private static string BuildDisclaimer(string sourceUrl)
    {
        const string text =
            "Dieses Rezept stammt aus dem Web und wurde **nicht** auf Dialyse- oder Transplantationsverträglichkeit geprüft. Vor dem Kochen bitte selbst prüfen.";

        return string.IsNullOrWhiteSpace(sourceUrl)
            ? text
            : $"{text}\n[🔗 Original-Rezept]({sourceUrl})";
    }

    private static string FormatIngredients(IReadOnlyCollection<string> ingredients)
        => string.Join("\n", ingredients.Where(i => !string.IsNullOrWhiteSpace(i)).Select(i => $"• {i}"));

    private static string FormatInstructions(IReadOnlyCollection<string> instructions)
        => string.Join("\n", instructions.Where(i => !string.IsNullOrWhiteSpace(i)).Select((inst, idx) => $"{idx + 1}. {inst}"));
}
