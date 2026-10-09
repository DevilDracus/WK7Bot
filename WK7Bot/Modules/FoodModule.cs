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
        => Context.Guild.FindTextChannel(TargetChannelName);

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
            if (string.IsNullOrWhiteSpace(query))
            {
                await FollowupAsync("❌ Bitte gib einen Suchbegriff an, z. B. `/recipe query:Pfannkuchen`.", ephemeral: true);
                return;
            }

            var targetChannel = FindFoodChannel();

            if (targetChannel == null)
            {
                await FollowupAsync($"❌ Der Kanal `#{TargetChannelName}` wurde auf diesem Server nicht gefunden.", ephemeral: true);
                return;
            }

            var recipe = await _recipeSearchService.SearchWebRecipeAsync(query);
            if (recipe == null)
            {
                await FollowupAsync(
                    $"❌ Kein auswertbares Rezept für **{query}** gefunden. Versuche andere Begriffe oder erstelle eins mit `/recipe-generate`.",
                    ephemeral: true);
                return;
            }

            recipe = await FormatWithGeminiAsync(recipe);

            var embed = RecipeEmbedBuilder.BuildSearched(
                recipe, $"Angefragt von @{Context.User.Username} • Rezept aus dem Web");

            await PostToFoodChannelAsync(targetChannel, embed);
            await FollowupAsync(
                $"✅ Rezept gefunden und in {targetChannel.Mention} mit Link zur Quelle veröffentlicht.",
                ephemeral: true);
        }
        catch (RecipeSearchUnavailableException ex)
        {
            _logger.LogWarning(ex, "Recipe search providers are unavailable.");
            await FollowupAsync(
                "❌ Die Rezeptsuche ist vorübergehend nicht verfügbar (der Suchanbieter blockiert Anfragen). Bitte versuche es in wenigen Minuten erneut oder nutze `/recipe-generate`.",
                ephemeral: true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Error occurred while executing the /recipe search command.");
            await FollowupAsync("❌ Bei der Rezeptsuche ist ein unerwarteter Fehler aufgetreten.", ephemeral: true);
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
                await FollowupAsync($"❌ Der Kanal `#{TargetChannelName}` wurde auf diesem Server nicht gefunden.", ephemeral: true);
                return;
            }

            var produce = await _geminiFoodService.GetSeasonalProduceAsync(DateTime.Now);
            var seasonalTerms = SeasonalTermPicker.Pick(produce, _randomSource);

            if (seasonalTerms.Count == 0)
            {
                await FollowupAsync("❌ Die Saisonprodukte konnten nicht von der Gemini-API abgerufen werden. Bitte versuche es später erneut.", ephemeral: true);
                return;
            }

            var searchQuery = string.IsNullOrWhiteSpace(query)
                ? string.Join(" ", seasonalTerms)
                : $"{query.Trim()} {seasonalTerms[0]}";

            var recipe = await _recipeSearchService.SearchWebRecipeAsync(searchQuery);
            if (recipe == null)
            {
                await FollowupAsync(
                    $"❌ Kein auswertbares Rezept für **{searchQuery}** gefunden. Versuche `/recipe` mit eigenem Begriff oder `/recipe-generate`.",
                    ephemeral: true);
                return;
            }

            // The Gemini formatting pass returns a fresh object, so the seasonal metadata is stamped
            // once after formatting instead of before and after it.
            recipe = await FormatWithGeminiAsync(recipe);
            recipe.SeasonalIngredientsUsed = seasonalTerms.ToList();

            var embed = RecipeEmbedBuilder.BuildSearched(
                recipe, $"Angefragt von @{Context.User.Username} • Saisonale Zutaten: {string.Join(", ", seasonalTerms)}");

            await PostToFoodChannelAsync(targetChannel, embed);
            await FollowupAsync(
                $"✅ Saisonales Rezept (**{searchQuery}**) in {targetChannel.Mention} mit Link zur Quelle veröffentlicht.",
                ephemeral: true);
        }
        catch (RecipeSearchUnavailableException ex)
        {
            _logger.LogWarning(ex, "Recipe search providers are unavailable.");
            await FollowupAsync(
                "❌ Die Rezeptsuche ist vorübergehend nicht verfügbar (der Suchanbieter blockiert Anfragen). Bitte versuche es in wenigen Minuten erneut oder nutze `/recipe-generate`.",
                ephemeral: true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Error occurred while executing the /recipe-seasonal slash command.");
            await FollowupAsync("❌ Bei der Suche nach einem saisonalen Rezept ist ein unerwarteter Fehler aufgetreten.", ephemeral: true);
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
                await FollowupAsync($"❌ Der Kanal `#{TargetChannelName}` wurde auf diesem Server nicht gefunden.", ephemeral: true);
                return;
            }

            var recipe = await _geminiFoodService.GetWeeklyRenalRecipeAsync(DateTime.Now);
            if (recipe == null)
            {
                await FollowupAsync("❌ Das Rezept konnte von der Gemini-API nicht erstellt werden. Bitte prüfe den API-Schlüssel und versuche es erneut.", ephemeral: true);
                return;
            }

            var embed = RecipeEmbedBuilder.BuildGenerated(
                recipe, $"Angefragt von @{Context.User.Username} • Für Dialyse & Nierentransplantation");

            await PostToFoodChannelAsync(targetChannel, embed);
            await FollowupAsync($"✅ Rezept erfolgreich erstellt und in {targetChannel.Mention} veröffentlicht!", ephemeral: true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Error occurred while executing /recipe-generate slash command.");
            await FollowupAsync("❌ Bei der Rezepterstellung ist ein unerwarteter Fehler aufgetreten.", ephemeral: true);
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
                await FollowupAsync($"❌ Der Kanal `#{TargetChannelName}` wurde auf diesem Server nicht gefunden.", ephemeral: true);
                return;
            }

            int targetMonth = DateTime.Now.Month;
            if (monthInput.HasValue)
            {
                if (monthInput.Value < 1 || monthInput.Value > 12)
                {
                    await FollowupAsync("❌ Ungültiger Monat. Bitte gib einen Wert zwischen 1 und 12 ein.", ephemeral: true);
                    return;
                }
                targetMonth = monthInput.Value;
            }

            // Construct target date for the selected month in the current year
            var targetDate = new DateTime(DateTime.Now.Year, targetMonth, 1);
            var produceData = await _geminiFoodService.GetSeasonalProduceAsync(targetDate);

            if (produceData == null)
            {
                await FollowupAsync("❌ Die Saisonprodukte konnten nicht von der Gemini-API abgerufen werden. Bitte versuche es später erneut.", ephemeral: true);
                return;
            }

            var embed = SeasonalProduceEmbedBuilder.Build(
                produceData,
                targetDate.GermanMonthName(),
                $"Angefragt von @{Context.User.Username} • Regionale Saisonware");

            await PostToFoodChannelAsync(targetChannel, embed);
            await FollowupAsync($"✅ Saisonale Übersicht für **{produceData.Month ?? targetDate.GermanMonthName()}** erfolgreich in {targetChannel.Mention} veröffentlicht!", ephemeral: true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Error occurred while executing /seasonal-produce slash command.");
            await FollowupAsync("❌ Bei der Erstellung der saisonalen Übersicht ist ein unerwarteter Fehler aufgetreten.", ephemeral: true);
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
