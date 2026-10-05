using System.Globalization;

namespace WK7Bot.Modules;

using System;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using Discord.Interactions;
using Microsoft.Extensions.Logging;
using WK7Bot.Services.Interfaces;

/// <summary>
/// Provides slash command interactions for food, seasonal produce, and renal-friendly weekly recipes.
/// </summary>
public class FoodModule : InteractionModuleBase<SocketInteractionContext>
{
    private readonly IGeminiFoodService _geminiFoodService;
    private readonly ILogger<FoodModule> _logger;

    private const string TargetChannelName = "🍎food";

    /// <summary>
    /// Initializes a new instance of the <see cref="FoodModule"/> class.
    /// </summary>
    /// <param name="geminiFoodService">The Gemini food service instance.</param>
    /// <param name="logger">The logger instance.</param>
    public FoodModule(IGeminiFoodService geminiFoodService, ILogger<FoodModule> logger)
    {
        _geminiFoodService = geminiFoodService ?? throw new ArgumentNullException(nameof(geminiFoodService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Generates a renal and transplant-friendly recipe based on current seasonal produce and posts it to the #🍎food channel.
    /// </summary>
    /// <returns>A task tracking the asynchronous command execution.</returns>
    [SlashCommand("recipe", "Generates a seasonal renal & transplant-safe recipe and posts it to #🍎food.")]
    public async Task GenerateRecipeAsync()
    {
        // Defer response to allow time for the Gemini API request
        await DeferAsync(ephemeral: true);

        try
        {
            var targetChannel = Context.Guild?.TextChannels
                .FirstOrDefault(c => string.Equals(c.Name, TargetChannelName, StringComparison.OrdinalIgnoreCase));

            if (targetChannel == null)
            {
                await FollowupAsync($"❌ Could not find the channel `#{TargetChannelName}` in this server.", ephemeral: true);
                return;
            }

            var recipe = await _geminiFoodService.GetWeeklyRenalRecipeAsync(DateTime.Now);
            if (recipe == null)
            {
                await FollowupAsync("❌ Failed to generate a recipe from Gemini API. Please verify the API key and try again.", ephemeral: true);
                return;
            }

            var n = recipe.Nutrition;
            string nutritionSummary = $"• **Calories:** {n.CaloriesKcal} kcal\n" +
                                     $"• **Protein:** {n.ProteinGrams} g | **Carbs:** {n.CarbohydratesGrams} g | **Fat:** {n.FatGrams} g\n" +
                                     $"• **Sodium:** {n.SodiumMg} mg\n" +
                                     $"• **Potassium (Kalium):** {n.PotassiumKaliumMg} mg\n" +
                                     $"• **Sulfate:** {n.SulfateMg} mg\n" +
                                     $"• **Phosphorus:** {n.PhosphorusMg} mg";

            string ingredientsFormatted = string.Join("\n", recipe.Ingredients.Select(i => $"• {i}"));
            string instructionsFormatted = string.Join("\n", recipe.Instructions.Select((inst, idx) => $"{idx + 1}. {inst}"));

            var embed = new EmbedBuilder()
                .WithTitle($"🥗 Recipe of the Week: {recipe.Title}")
                .WithDescription($"{recipe.Description}\n\n⏱️ **Prep Time:** {recipe.PrepTime} | 🍳 **Cook Time:** {recipe.CookTime} | 🍽️ **Servings:** {recipe.Servings}")
                .WithColor(Color.Teal)
                .AddField("🛒 Ingredients", ingredientsFormatted.Length > 1024 ? ingredientsFormatted[..1021] + "..." : ingredientsFormatted, false)
                .AddField("👨‍🍳 Preparation Steps", instructionsFormatted.Length > 1024 ? instructionsFormatted[..1021] + "..." : instructionsFormatted, false)
                .AddField("📊 Nutrition per Serving", nutritionSummary, false)
                .AddField("🛡️ Safety & Renal Notes", recipe.TransplantSafetyNotes, false)
                .WithFooter($"Requested by @{Context.User.Username} • Dialysis & Kidney Transplant Safe")
                .WithCurrentTimestamp()
                .Build();

            await targetChannel.SendMessageAsync(embed: embed);
            await FollowupAsync($"✅ Recipe successfully created and posted to {targetChannel.Mention}!", ephemeral: true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred while executing /recipe slash command.");
            await FollowupAsync("❌ An unexpected error occurred while generating the recipe.", ephemeral: true);
        }
    }
    
    /// <summary>
    /// Generates seasonal produce information for a specified month (or current month) and posts it to the #🍎food channel.
    /// </summary>
    /// <param name="monthInput">Optional month number (1-12). Defaults to the current month if omitted.</param>
    /// <returns>A task tracking the asynchronous command execution.</returns>
    [SlashCommand("seasonal-produce", "Displays seasonal fruits, vegetables, herbs, and nuts for a given month in #🍎food.")]
    public async Task GetSeasonalProduceAsync(
        [Summary("month", "The month number (1-12). Leave empty for the current month.")] int? monthInput = null)
    {
        await DeferAsync(ephemeral: true);

        try
        {
            var targetChannel = Context.Guild?.TextChannels
                .FirstOrDefault(c => string.Equals(c.Name, TargetChannelName, StringComparison.OrdinalIgnoreCase));

            if (targetChannel == null)
            {
                await FollowupAsync($"❌ Could not find the channel `#{TargetChannelName}` in this server.", ephemeral: true);
                return;
            }

            int targetMonth = DateTime.Now.Month;
            if (monthInput.HasValue)
            {
                if (monthInput.Value < 1 || monthInput.Value > 12)
                {
                    await FollowupAsync("❌ Invalid month provided. Please enter a value between 1 and 12.", ephemeral: true);
                    return;
                }
                targetMonth = monthInput.Value;
            }

            // Construct target date for the selected month in the current year
            var targetDate = new DateTime(DateTime.Now.Year, targetMonth, 1);
            var produceData = await _geminiFoodService.GetSeasonalProduceAsync(targetDate);

            if (produceData == null)
            {
                await FollowupAsync("❌ Failed to retrieve seasonal produce from Gemini API. Please try again later.", ephemeral: true);
                return;
            }

            string monthName = targetDate.ToString("MMMM", CultureInfo.GetCultureInfo("de-DE"));
            string fruitsFormatted = produceData.Fruits.Count > 0 ? string.Join(", ", produceData.Fruits) : "Keine angegeben";
            string vegetablesFormatted = produceData.Vegetables.Count > 0 ? string.Join(", ", produceData.Vegetables) : "Keine angegeben";
            string herbsFormatted = produceData.Herbs.Count > 0 ? string.Join(", ", produceData.Herbs) : "Keine angegeben";
            string nutsFormatted = produceData.Nuts.Count > 0 ? string.Join(", ", produceData.Nuts) : "Keine angegeben";

            var embed = new EmbedBuilder()
                .WithTitle($"🌱 Saisonkalender: {produceData.Month ?? monthName}")
                .WithDescription($"Übersicht der regionalen Saisonprodukte (Zentraleuropa / Leipzig-Region) für **{monthName}**.")
                .WithColor(Color.Green)
                .AddField("🍎 Obst", fruitsFormatted, false)
                .AddField("🥕 Gemüse", vegetablesFormatted, false)
                .AddField("🌿 Kräuter", herbsFormatted, false)
                .AddField("🌰 Nüsse", nutsFormatted, false)
                .WithFooter($"Requested by @{Context.User.Username} • Regionale Saisonware")
                .WithCurrentTimestamp()
                .Build();

            await targetChannel.SendMessageAsync(embed: embed);
            await FollowupAsync($"✅ Seasonal produce overview for **{monthName}** successfully posted to {targetChannel.Mention}!", ephemeral: true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred while executing /seasonal-produce slash command.");
            await FollowupAsync("❌ An unexpected error occurred while generating the seasonal produce list.", ephemeral: true);
        }
    }
}