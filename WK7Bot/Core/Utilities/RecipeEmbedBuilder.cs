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
    private const int FieldValueLimit = 1024;

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
    /// Shared embed assembly for both recipe sources.
    /// </summary>
    private static Embed Build(RenalRecipeData recipe, string title, Color color, string footer, bool includeDisclaimer)
    {
        ArgumentNullException.ThrowIfNull(recipe);

        var builder = new EmbedBuilder()
            .WithTitle(title)
            .WithDescription(BuildDescription(recipe))
            .WithColor(color);

        var ingredients = FormatIngredients(recipe.Ingredients);
        if (ingredients.Length > 0)
        {
            builder.AddField("🛒 Ingredients", Truncate(ingredients), false);
        }

        var instructions = FormatInstructions(recipe.Instructions);
        if (instructions.Length > 0)
        {
            builder.AddField("👨‍🍳 Preparation Steps", Truncate(instructions), false);
        }

        var dietTags = recipe.DietTags ?? new List<string>();
        if (dietTags.Count > 0)
        {
            var formattedTags = DietTagFormatter.Format(dietTags);
            if (formattedTags.Length > 0)
            {
                builder.AddField("🏷️ Diet Tags", formattedTags, false);
            }
        }

        if (!string.IsNullOrWhiteSpace(recipe.TransplantSafetyNotes))
        {
            builder.AddField("🛡️ Safety & Renal Notes", recipe.TransplantSafetyNotes, false);
        }

        var seasonalIngredients = recipe.SeasonalIngredientsUsed ?? new List<string>();
        if (seasonalIngredients.Count > 0)
        {
            builder.AddField("🌿 Saisonale Zutaten", Truncate(string.Join(", ", seasonalIngredients)), false);
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

            builder.AddField("⚠️ Hinweis", BuildDisclaimer(recipe.SourceUrl), false);
        }

        if (!string.IsNullOrWhiteSpace(footer))
        {
            builder.WithFooter(footer);
        }

        builder.WithCurrentTimestamp();
        return builder.Build();
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

    /// <summary>
    /// Cuts a field value to Discord's 1024 character limit, appending an ellipsis when truncated.
    /// </summary>
    private static string Truncate(string value)
        => value.Length > FieldValueLimit ? value[..(FieldValueLimit - 3)] + "..." : value;
}
