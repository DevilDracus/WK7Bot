namespace WK7Bot.Core.Utilities;

using System;
using System.Text;
using Discord;
using WK7Bot.Models;

/// <summary>
/// Builds the Discord embed for an Open Food Facts product with renal safety indicators, shared by the
/// <c>/check-food</c> command so the layout stays consistent.
/// </summary>
public static class OpenFoodFactsEmbedBuilder
{
    private const double PotassiumHighThresholdMg = 300;
    private const double PhosphorusHighThresholdMg = 200;
    private const double SodiumHighThresholdMg = 400;
    /// <summary>
    /// Builds the embed for the supplied product.
    /// </summary>
    /// <param name="product">The product data from Open Food Facts.</param>
    /// <returns>The rendered embed.</returns>
    public static Embed Build(OpenFoodFactsProduct product)
        => BuildEmbedBuilder(product).Build();

    /// <summary>
    /// Builds the embed builder for the supplied product without rendering it to an <see cref="Embed"/>.
    /// </summary>
    /// <param name="product">The product data from Open Food Facts.</param>
    /// <returns>The configured embed builder.</returns>
    public static EmbedBuilder BuildEmbedBuilder(OpenFoodFactsProduct product)
    {
        ArgumentNullException.ThrowIfNull(product);

        var embed = new EmbedBuilder()
            .WithTitle($"🥜 {product.ProductName}")
            .WithColor(Color.Green)
            .WithTimestamp(DateTimeOffset.UtcNow)
            .WithFooter("Daten von Open Food Facts • Pro 100 g");

        if (!string.IsNullOrWhiteSpace(product.Brands))
        {
            embed.AddField("Marke", product.Brands, inline: true);
        }

        if (!string.IsNullOrWhiteSpace(product.Quantity))
        {
            embed.AddField("Menge", product.Quantity, inline: true);
        }

        if (!string.IsNullOrWhiteSpace(product.NutriScoreGrade))
        {
            var scoreUpper = product.NutriScoreGrade.ToUpperInvariant();
            var scoreEmoji = scoreUpper switch
            {
                "A" => "🟢",
                "B" => "🟡",
                "C" => "🟠",
                "D" => "🔴",
                "E" => "⚫",
                _ => "❓"
            };
            embed.AddField("Nutri-Score", $"{scoreEmoji} {scoreUpper}", inline: true);
        }

        var n = product.Nutriments;
        var nutritionText = BuildNutritionText(n);

        if (nutritionText.Length > 0)
        {
            embed.AddField("📊 Nährwerte (pro 100 g)", nutritionText.ToString(), inline: false);
        }

        // Renal safety assessment
        var renalText = BuildRenalSafetyText(n);

        if (renalText.Length > 0)
        {
            embed.AddField("🩺 Nieren-Sicherheit", renalText.ToString(), inline: false);
        }
        else
        {
            embed.AddField("🩺 Nieren-Sicherheit", "Keine Kalium-/Phosphor-/Natrium-Angaben vorhanden.", inline: false);
        }

        if (!string.IsNullOrWhiteSpace(product.Url))
        {
            embed.WithUrl(product.Url);
        }

        if (!string.IsNullOrWhiteSpace(product.ImageUrl))
        {
            embed.WithThumbnailUrl(product.ImageUrl);
        }

        return embed;
    }

    private static StringBuilder BuildNutritionText(OpenFoodFactsNutriments n)
    {
        var nutritionText = new StringBuilder();
        if (n.EnergyKcal.HasValue)
            nutritionText.AppendLine($"🔥 **Kalorien:** {n.EnergyKcal.Value:F0} kcal");
        if (n.Proteins.HasValue)
            nutritionText.AppendLine($"🥩 **Eiweiß:** {n.Proteins.Value:F1} g");
        if (n.Carbohydrates.HasValue)
            nutritionText.AppendLine($"🥤 **Kohlenhydrate:** {n.Carbohydrates.Value:F1} g");
        if (n.Sugars.HasValue)
            nutritionText.AppendLine($"🍬 **Zucker:** {n.Sugars.Value:F1} g");
        if (n.Fat.HasValue)
            nutritionText.AppendLine($"🧈 **Fett:** {n.Fat.Value:F1} g");
        if (n.SaturatedFat.HasValue)
            nutritionText.AppendLine($"🧅 **Gesättigte Fettsäuren:** {n.SaturatedFat.Value:F1} g");
        if (n.Salt.HasValue)
            nutritionText.AppendLine($"🧂 **Salz:** {n.Salt.Value:F2} g");
        if (n.Fiber.HasValue)
            nutritionText.AppendLine($"🌿 **Ballaststoffe:** {n.Fiber.Value:F1} g");

        return nutritionText;
    }

    private static StringBuilder BuildRenalSafetyText(OpenFoodFactsNutriments n)
    {
        var renalText = new StringBuilder();

        if (n.Potassium.HasValue)
        {
            var potassiumMg = n.Potassium.Value * 1000;
            var status = potassiumMg > PotassiumHighThresholdMg ? "⚠️ Hoch (Nieren-Vorsicht)" : "✅ Niedrig";
            renalText.AppendLine($"**Kalium:** {potassiumMg:F0} mg — {status}");
        }

        if (n.Phosphorus.HasValue)
        {
            var phosphorusMg = n.Phosphorus.Value * 1000;
            var status = phosphorusMg > PhosphorusHighThresholdMg ? "⚠️ Hoch (Nieren-Vorsicht)" : "✅ Niedrig";
            renalText.AppendLine($"**Phosphor:** {phosphorusMg:F0} mg — {status}");
        }

        if (n.Sodium.HasValue)
        {
            var sodiumMg = n.Sodium.Value * 1000;
            var status = sodiumMg > SodiumHighThresholdMg ? "⚠️ Hoch (Nieren-Vorsicht)" : "✅ Niedrig";
            renalText.AppendLine($"**Natrium:** {sodiumMg:F0} mg — {status}");
        }

        return renalText;
    }

    /// <summary>
    /// Alias for <see cref="BuildEmbedBuilder"/> matching the original helper name.
    /// </summary>
    /// <param name="product">The product data from Open Food Facts.</param>
    /// <returns>The configured embed builder.</returns>
    public static EmbedBuilder BuildOpenFoodFactsEmbed(OpenFoodFactsProduct product)
        => BuildEmbedBuilder(product);
}
