using WK7Bot.Core.Utilities;
using WK7Bot.Models;
using Xunit;

namespace WK7Bot.Tests;

public class RecipeEmbedBuilderTests
{
    private static RenalRecipeData CreateRecipe() => new()
    {
        Title = "Gemüseauflauf",
        Description = "Saisonales Auflaufgericht mit renalen Zutaten.",
        PrepTime = "20 Minuten",
        CookTime = "40 Minuten",
        Servings = 4,
        Ingredients = new List<string> { "Zucchini", "Karotten" },
        Instructions = new List<string> { "Gemüse schneiden", "Im Ofen backen" },
        TransplantSafetyNotes = "Vollständig durchgaren.",
        DietTags = new List<string> { "low_potassium" }
    };

    [Fact]
    public void BuildGenerated_KeepsDietAndSafetySections_AndSkipsDisclaimer()
    {
        var embed = RecipeEmbedBuilder.BuildGenerated(CreateRecipe(), "Requested by @Tester");

        Assert.Equal("🥗 Recipe of the Week: Gemüseauflauf", embed.Title);
        Assert.Null(embed.Url);
        Assert.Contains(embed.Fields, f => f.Name == "🏷️ Diet Tags" && f.Value.Contains("Kaliumarm"));
        Assert.Contains(embed.Fields, f => f.Name == "🛡️ Safety & Renal Notes" && f.Value.Contains("Vollständig durchgaren."));
        Assert.DoesNotContain(embed.Fields, f => f.Name == "⚠️ Hinweis");
        Assert.Contains("Prep Time:** 20 Minuten", embed.Description);
        Assert.Contains("Cook Time:** 40 Minuten", embed.Description);
        Assert.Contains("Servings:** 4", embed.Description);
        Assert.Equal("Requested by @Tester", embed.Footer!.Value.Text);
    }

    [Fact]
    public void BuildSearched_LinksSource_AndAddsDisclaimer()
    {
        var recipe = CreateRecipe();
        recipe.SourceUrl = "https://www.chefkoch.de/rezepte/123/pfannkuchen.html";
        recipe.ImageUrl = "https://images.example.com/auflauf.jpg";
        recipe.DietTags = new List<string>();
        recipe.TransplantSafetyNotes = string.Empty;

        var embed = RecipeEmbedBuilder.BuildSearched(recipe, "Requested by @Tester • Rezept aus dem Web");

        Assert.Equal("🍽️ Gemüseauflauf", embed.Title);
        Assert.Equal(recipe.SourceUrl, embed.Url?.ToString());
        Assert.Equal(recipe.ImageUrl, embed.Thumbnail?.Url);
        Assert.Contains(embed.Fields, f => f.Name == "⚠️ Hinweis" && f.Value.Contains(recipe.SourceUrl));
        Assert.DoesNotContain(embed.Fields, f => f.Name == "🏷️ Diet Tags");
        Assert.DoesNotContain(embed.Fields, f => f.Name == "🛡️ Safety & Renal Notes");
    }

    [Fact]
    public void BuildSearched_OmitsEmptySections()
    {
        var recipe = new RenalRecipeData
        {
            Title = "Klassensalat",
            SourceUrl = "https://example.org/salat"
        };

        var embed = RecipeEmbedBuilder.BuildSearched(recipe, string.Empty);

        Assert.DoesNotContain(embed.Fields, f => f.Name == "🛒 Ingredients");
        Assert.DoesNotContain(embed.Fields, f => f.Name == "👨‍🍳 Preparation Steps");
        Assert.Contains(embed.Fields, f => f.Name == "⚠️ Hinweis");
        Assert.Null(embed.Footer);
        Assert.Equal("🍽️ Klassensalat", embed.Title);
    }

    [Fact]
    public void BuildGenerated_ChunksOversizedField_WithoutLosingContent()
    {
        var recipe = CreateRecipe();
        recipe.Ingredients = new List<string> { new('x', 2000) };

        var embed = RecipeEmbedBuilder.BuildGenerated(recipe, "footer");

        var fields = embed.Fields
            .Where(f => f.Name.StartsWith("🛒 Ingredients", StringComparison.Ordinal))
            .ToArray();

        Assert.Equal(2, fields.Length);
        Assert.All(fields, f => Assert.True(f.Value.Length <= 1024));
        Assert.Equal(1024, fields[0].Value.Length);

        // Chunking replaces truncation: every character of the original section survives.
        Assert.Equal("• " + new string('x', 2000), string.Concat(fields.Select(f => f.Value)));
    }
}
