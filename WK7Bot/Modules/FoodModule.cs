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
}