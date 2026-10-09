using System.Globalization;

namespace WK7Bot.Modules;

using System;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using Discord.Interactions;
using Microsoft.Extensions.Logging;
using WK7Bot.Core.Exceptions;
using WK7Bot.Core.Utilities;
using WK7Bot.Models;
using WK7Bot.Services;
using WK7Bot.Services.Interfaces;

/// <summary>
/// Provides slash command interactions for food, seasonal produce, and renal-friendly weekly recipes.
/// </summary>
public class FoodModule : InteractionModuleBase<SocketInteractionContext>
{
    private readonly IGeminiFoodService _geminiFoodService;
    private readonly IRecipeSearchService _recipeSearchService;
    private readonly IRandomSource _randomSource;
    private readonly ILogger<FoodModule> _logger;

    private const string TargetChannelName = "🍎food";

    /// <summary>
    /// Initializes a new instance of the <see cref="FoodModule"/> class.
    /// </summary>
    /// <param name="geminiFoodService">The Gemini food service instance.</param>
    /// <param name="recipeSearchService">The web recipe search service instance.</param>
    /// <param name="logger">The logger instance.</param>
    /// <param name="randomSource">The randomness used to pick seasonal terms; defaults to <see cref="SystemRandomSource"/>.</param>
    public FoodModule(
        IGeminiFoodService geminiFoodService,
        IRecipeSearchService recipeSearchService,
        ILogger<FoodModule> logger,
        IRandomSource? randomSource = null)
    {
        _geminiFoodService = geminiFoodService ?? throw new ArgumentNullException(nameof(geminiFoodService));
        _recipeSearchService = recipeSearchService ?? throw new ArgumentNullException(nameof(recipeSearchService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _randomSource = randomSource ?? new SystemRandomSource();
    }

    /// <summary>
    /// Resolves the <c>#🍎food</c> text channel inside the guild the command originated from.
    /// </summary>
    /// <returns>The matching text channel, or <see langword="null"/> when the guild or channel is unavailable.</returns>
    protected virtual ITextChannel? FindFoodChannel()
        => Context.Guild?.TextChannels
            .FirstOrDefault(c => string.Equals(c.Name, TargetChannelName, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Publishes the generated embed to the target food channel.
    /// </summary>
    /// <param name="channel">The channel that receives the embed.</param>
    /// <param name="embed">The embed to publish.</param>
    /// <returns>A task that completes once the message has been sent.</returns>
    protected virtual Task PostToFoodChannelAsync(ITextChannel channel, Embed embed)
        => channel.SendMessageAsync(embed: embed);

    /// <summary>
    /// Searches the web for a recipe matching the query, parses the structured recipe data of the best candidate and
    /// posts it together with a link to the original source into the #🍎food channel.
    /// </summary>
    /// <param name="query">The free text search query (e.g. "Pfannkuchen").</param>
    /// <returns>A task tracking the asynchronous command execution.</returns>
    [SlashCommand("recipe", "Searches the web for a recipe and posts it with its source link to #🍎food.")]
    public async Task SearchRecipeAsync(
        [Summary("query", "What should be cooked? e.g. \"Pfannkuchen\"")] string query)
    {
        // Defer response to allow time for the search and recipe page downloads
        await DeferAsync(ephemeral: true);

        try
        {
            var targetChannel = FindFoodChannel();

            if (targetChannel == null)
            {
                await FollowupAsync($"❌ Could not find the channel `#{TargetChannelName}` in this server.", ephemeral: true);
                return;
            }

            if (string.IsNullOrWhiteSpace(query))
            {
                await FollowupAsync("❌ Please provide a search query, e.g. `/recipe query:Pfannkuchen`.", ephemeral: true);
                return;
            }

            var recipe = await _recipeSearchService.SearchWebRecipeAsync(query);
            if (recipe == null)
            {
                await FollowupAsync(
                    $"❌ No parseable recipe found for **{query}**. Try different wording or generate one with `/recipe-generate`.",
                    ephemeral: true);
                return;
            }

            recipe = await FormatWithGeminiAsync(recipe);

            var embed = RecipeEmbedBuilder.BuildSearched(
                recipe, $"Requested by @{Context.User.Username} • Rezept aus dem Web");

            await PostToFoodChannelAsync(targetChannel, embed);
            await FollowupAsync(
                $"✅ Recipe found and posted to {targetChannel.Mention} with a link to the source.",
                ephemeral: true);
        }
        catch (RecipeSearchUnavailableException ex)
        {
            _logger.LogWarning(ex, "Recipe search providers are unavailable.");
            await FollowupAsync(
                "❌ The recipe search is temporarily unavailable (search provider blocking requests). Please try again in a few minutes or use `/recipe-generate`.",
                ephemeral: true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred while executing the /recipe search command.");
            await FollowupAsync("❌ An unexpected error occurred while searching for the recipe.", ephemeral: true);
        }
    }

    /// <summary>
    /// Looks up a web recipe seeded with the current season's produce (vegetables and fruits), parses it, normalises
    /// it via Gemini and posts it together with a link to the original source into the #🍎food channel.
    /// </summary>
    /// <param name="query">Optional dish hint (e.g. "Pfannkuchen"); one seasonal ingredient is appended so the search stays seasonal.</param>
    /// <returns>A task tracking the asynchronous command execution.</returns>
    [SlashCommand("recipe-seasonal", "Finds a web recipe using this season's produce and posts it with its source link to #🍎food.")]
    public async Task SearchSeasonalRecipeAsync(
        [Summary("query", "Optional dish hint, e.g. \"Pfannkuchen\". Leave empty for a purely seasonal search.")] string? query = null)
    {
        await DeferAsync(ephemeral: true);

        try
        {
            var targetChannel = FindFoodChannel();

            if (targetChannel == null)
            {
                await FollowupAsync($"❌ Could not find the channel `#{TargetChannelName}` in this server.", ephemeral: true);
                return;
            }

            var produce = await _geminiFoodService.GetSeasonalProduceAsync(DateTime.Now);
            var seasonalTerms = SeasonalTermPicker.Pick(produce, _randomSource);

            if (seasonalTerms.Count == 0)
            {
                await FollowupAsync("❌ Failed to retrieve seasonal produce from Gemini API. Please try again later.", ephemeral: true);
                return;
            }

            var searchQuery = string.IsNullOrWhiteSpace(query)
                ? string.Join(" ", seasonalTerms)
                : $"{query.Trim()} {seasonalTerms[0]}";

            var recipe = await _recipeSearchService.SearchWebRecipeAsync(searchQuery);
            if (recipe == null)
            {
                await FollowupAsync(
                    $"❌ No parseable recipe found for **{searchQuery}**. Try `/recipe` with your own query or `/recipe-generate`.",
                    ephemeral: true);
                return;
            }

            recipe.SeasonalIngredientsUsed = seasonalTerms.ToList();
            recipe = await FormatWithGeminiAsync(recipe);
            recipe.SeasonalIngredientsUsed = seasonalTerms.ToList();

            var embed = RecipeEmbedBuilder.BuildSearched(
                recipe, $"Requested by @{Context.User.Username} • Saisonale Zutaten: {string.Join(", ", seasonalTerms)}");

            await PostToFoodChannelAsync(targetChannel, embed);
            await FollowupAsync(
                $"✅ Seasonal recipe (**{searchQuery}**) posted to {targetChannel.Mention} with a link to the source.",
                ephemeral: true);
        }
        catch (RecipeSearchUnavailableException ex)
        {
            _logger.LogWarning(ex, "Recipe search providers are unavailable.");
            await FollowupAsync(
                "❌ The recipe search is temporarily unavailable (search provider blocking requests). Please try again in a few minutes or use `/recipe-generate`.",
                ephemeral: true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred while executing the /recipe-seasonal slash command.");
            await FollowupAsync("❌ An unexpected error occurred while searching for a seasonal recipe.", ephemeral: true);
        }
    }

    /// <summary>
    /// Generates a renal and transplant-friendly recipe based on current seasonal produce and posts it to the #🍎food channel.
    /// </summary>
    /// <returns>A task tracking the asynchronous command execution.</returns>
    [SlashCommand("recipe-generate", "Generates a seasonal renal & transplant-safe recipe and posts it to #🍎food.")]
    public async Task GenerateRecipeAsync()
    {
        // Defer response to allow time for the Gemini API request
        await DeferAsync(ephemeral: true);

        try
        {
            var targetChannel = FindFoodChannel();

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

            var embed = RecipeEmbedBuilder.BuildGenerated(
                recipe, $"Requested by @{Context.User.Username} • Dialysis & Kidney Transplant Safe");

            await PostToFoodChannelAsync(targetChannel, embed);
            await FollowupAsync($"✅ Recipe successfully created and posted to {targetChannel.Mention}!", ephemeral: true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred while executing /recipe-generate slash command.");
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
            var targetChannel = FindFoodChannel();

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
                .WithTitle(EmbedText.Title($"🌱 Saisonkalender: {produceData.Month ?? monthName}"))
                .WithDescription($"Übersicht der regionalen Saisonprodukte (Zentraleuropa / Leipzig-Region) für **{monthName}**.")
                .WithColor(Color.Green)
                .AddField("🍎 Obst", EmbedText.Field(fruitsFormatted), false)
                .AddField("🥕 Gemüse", EmbedText.Field(vegetablesFormatted), false)
                .AddField("🌿 Kräuter", EmbedText.Field(herbsFormatted), false)
                .AddField("🌰 Nüsse", EmbedText.Field(nutsFormatted), false)
                .WithFooter($"Requested by @{Context.User.Username} • Regionale Saisonware")
                .WithCurrentTimestamp()
                .Build();

            await PostToFoodChannelAsync(targetChannel, embed);
            await FollowupAsync($"✅ Seasonal produce overview for **{monthName}** successfully posted to {targetChannel.Mention}!", ephemeral: true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred while executing /seasonal-produce slash command.");
            await FollowupAsync("❌ An unexpected error occurred while generating the seasonal produce list.", ephemeral: true);
        }
    }

    /// <summary>
    /// Normalises a scraped recipe with Gemini so it matches the bot's embed format. Formatting is best effort:
    /// when Gemini is unavailable, fails, or returns unusable data, the raw parsed recipe is used instead.
    /// </summary>
    /// <param name="recipe">The parsed recipe to normalise.</param>
    /// <returns>The formatted recipe, or the unchanged input when formatting is unavailable.</returns>
    private async Task<RenalRecipeData> FormatWithGeminiAsync(RenalRecipeData recipe)
    {
        try
        {
            var formatted = await _geminiFoodService.FormatRecipeAsync(recipe);
            return formatted ?? recipe;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Gemini recipe formatting failed; posting the raw parsed recipe instead.");
            return recipe;
        }
    }
}