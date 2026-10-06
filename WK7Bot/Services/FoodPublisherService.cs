using System.Globalization;

namespace WK7Bot.Services;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WK7Bot.Core.Utilities;
using WK7Bot.Models;
using WK7Bot.Options;
using WK7Bot.Services.Interfaces;

/// <summary>
/// Background worker service executing scheduled dispatches for monthly seasonal produce lists and the weekly seasonal
/// web recipe into Discord.
/// </summary>
public class FoodPublisherService : BackgroundService
{
    private readonly DiscordSocketClient _discordClient;
    private readonly IGeminiFoodService _geminiFoodService;
    private readonly IRecipeSearchService _recipeSearchService;
    private readonly IRandomSource _randomSource;
    private readonly Wk7BotOptions _options;
    private readonly ILogger<FoodPublisherService> _logger;

    private const string TargetChannelName = "🍎food";

    private DateTime? _lastMonthlyPostDate;
    private DateTime? _lastWeeklyRecipePostDate;

    /// <summary>
    /// Initializes a new instance of the <see cref="FoodPublisherService"/> class.
    /// </summary>
    /// <param name="discordClient">The Discord socket client connection instance.</param>
    /// <param name="geminiFoodService">The Gemini AI food and recipe service dependency.</param>
    /// <param name="recipeSearchService">The web recipe search service used for the weekly seasonal recipe.</param>
    /// <param name="options">Application options instance.</param>
    /// <param name="logger">Logger instance.</param>
    /// <param name="randomSource">Randomness used to pick the seasonal search terms; defaults to <see cref="SystemRandomSource"/>.</param>
    public FoodPublisherService(
        DiscordSocketClient discordClient,
        IGeminiFoodService geminiFoodService,
        IRecipeSearchService recipeSearchService,
        IOptions<Wk7BotOptions> options,
        ILogger<FoodPublisherService> logger,
        IRandomSource? randomSource = null)
    {
        _discordClient = discordClient ?? throw new ArgumentNullException(nameof(discordClient));
        _geminiFoodService = geminiFoodService ?? throw new ArgumentNullException(nameof(geminiFoodService));
        _recipeSearchService = recipeSearchService ?? throw new ArgumentNullException(nameof(recipeSearchService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _randomSource = randomSource ?? new SystemRandomSource();
    }

    /// <summary>
    /// Core polling loop continuously checking time anchors for monthly produce and Thursday recipe schedules.
    /// </summary>
    /// <param name="stoppingToken">Cancellation token used to interrupt processing during app shutdown.</param>
    /// <returns>A task tracking execution state.</returns>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Features.FoodServiceEnabled)
        {
            _logger.LogInformation("Food publisher background service is disabled in configuration.");
            return;
        }

        _logger.LogInformation("Food publisher background service started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var now = DateTime.Now;

                if (ShouldPublishMonthlyProduce(now))
                {
                    await PublishMonthlyProduceAsync(now, stoppingToken);
                }

                if (ShouldPublishWeeklyRecipe(now))
                {
                    await PublishWeeklyRecipeAsync(now, stoppingToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error encountered while checking or executing food background jobs.");
            }

            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        }
    }

    /// <summary>
    /// Determines whether the monthly produce post should trigger on the first day of the current month.
    /// </summary>
    private bool ShouldPublishMonthlyProduce(DateTime now)
    {
        if (now.Day != 1) return false;
        if (now.Hour != 9 || now.Minute != 0) return false;

        return !_lastMonthlyPostDate.HasValue || _lastMonthlyPostDate.Value.Month != now.Month;
    }

    /// <summary>
    /// Determines whether the weekly renal recipe should trigger on Thursday at 15:30.
    /// </summary>
    private bool ShouldPublishWeeklyRecipe(DateTime now)
    {
        if (now.DayOfWeek != DayOfWeek.Thursday) return false;
        if (now.Hour != 15 || now.Minute != 30) return false;

        return !_lastWeeklyRecipePostDate.HasValue || _lastWeeklyRecipePostDate.Value.Date != now.Date;
    }

    /// <summary>
    /// Queries seasonal produce data and posts the embedded payload to the target Discord channel.
    /// </summary>
    private async Task PublishMonthlyProduceAsync(DateTime now, CancellationToken cancellationToken)
    {
        var channel = ResolveTargetChannel();
        if (channel == null) return;

        _logger.LogInformation("Generating monthly seasonal produce list for {Month}", now.ToString("MMMM"));

        var produce = await _geminiFoodService.GetSeasonalProduceAsync(now, cancellationToken);
        if (produce == null) return;

        string monthName = DateTime.Today.ToString("MMMM", CultureInfo.GetCultureInfo("de-DE"));
        string fruitsFormatted = produce.Fruits.Count > 0 ? string.Join(", ", produce.Fruits) : "Keine angegeben";
        string vegetablesFormatted = produce.Vegetables.Count > 0 ? string.Join(", ", produce.Vegetables) : "Keine angegeben";
        string herbsFormatted = produce.Herbs.Count > 0 ? string.Join(", ", produce.Herbs) : "Keine angegeben";
        string nutsFormatted = produce.Nuts.Count > 0 ? string.Join(", ", produce.Nuts) : "Keine angegeben";
        
        var embed = new EmbedBuilder()
            .WithTitle($"🌱 Saisonkalender: {produce.Month ?? monthName}")
            .WithDescription($"Übersicht der regionalen Saisonprodukte (Zentraleuropa / Leipzig-Region) für **{monthName}**.")
            .WithColor(Color.Green)
            .AddField("🍎 Obst", fruitsFormatted, false)
            .AddField("🥕 Gemüse", vegetablesFormatted, false)
            .AddField("🌿 Kräuter", herbsFormatted, false)
            .AddField("🌰 Nüsse", nutsFormatted, false)
            .WithFooter($"Sent by WK7 Bot • Regionale Saisonware")
            .WithCurrentTimestamp()
            .Build();

        await channel.SendMessageAsync(embed: embed);
        _lastMonthlyPostDate = now;
    }

    /// <summary>
    /// Finds a seasonal web recipe (seeded with randomly weighted seasonal produce, formatted via Gemini) and posts it
    /// to the target channel. When the search yields nothing, the previously used Gemini-generated recipe is posted
    /// instead so the weekly slot is never empty.
    /// </summary>
    private async Task PublishWeeklyRecipeAsync(DateTime now, CancellationToken cancellationToken)
    {
        var channel = ResolveTargetChannel();
        if (channel == null) return;

        _logger.LogInformation("Searching a seasonal web recipe for {Date}", now.ToShortDateString());

        var seasonalTerms = SeasonalTermPicker.Pick(
            await _geminiFoodService.GetSeasonalProduceAsync(now, cancellationToken),
            _randomSource);

        var recipe = seasonalTerms.Count > 0
            ? await SearchSeasonalRecipeAsync(string.Join(" ", seasonalTerms), seasonalTerms, cancellationToken)
            : null;

        if (recipe != null)
        {
            var embed = RecipeEmbedBuilder.BuildSearched(
                recipe,
                $"Automatisch veröffentlicht • Saisonale Zutaten: {string.Join(", ", seasonalTerms)}");

            await channel.SendMessageAsync(embed: embed);
            _lastWeeklyRecipePostDate = now;
            return;
        }

        _logger.LogWarning(
            "No seasonal web recipe found for {Date}; falling back to a Gemini-generated recipe.",
            now.ToShortDateString());

        var generated = await _geminiFoodService.GetWeeklyRenalRecipeAsync(now, cancellationToken);
        if (generated == null) return;

        await channel.SendMessageAsync(
            embed: RecipeEmbedBuilder.BuildGenerated(generated, "Tailored for Dialysis & Kidney Transplant Safety"));
        _lastWeeklyRecipePostDate = now;
    }

    /// <summary>
    /// Runs the web search for the given seasonal query and normalises the parsed recipe via Gemini (best effort).
    /// </summary>
    /// <returns>The recipe with its seasonal ingredients stamped on, or <see langword="null"/> when nothing was found.</returns>
    private async Task<RenalRecipeData?> SearchSeasonalRecipeAsync(
        string searchQuery,
        IReadOnlyList<string> seasonalTerms,
        CancellationToken cancellationToken)
    {
        var recipe = await _recipeSearchService.SearchWebRecipeAsync(searchQuery, cancellationToken);
        if (recipe == null)
        {
            return null;
        }

        recipe.SeasonalIngredientsUsed = seasonalTerms.ToList();

        try
        {
            var formatted = await _geminiFoodService.FormatRecipeAsync(recipe, cancellationToken);
            if (formatted != null)
            {
                recipe = formatted;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Gemini recipe formatting failed; publishing the raw parsed recipe instead.");
        }

        recipe.SeasonalIngredientsUsed = seasonalTerms.ToList();
        return recipe;
    }

    /// <summary>
    /// Resolves the socket text channel matching the configured target channel name.
    /// </summary>
    private SocketTextChannel? ResolveTargetChannel()
    {
        foreach (var guild in _discordClient.Guilds)
        {
            var channel = guild.TextChannels.FirstOrDefault(c => string.Equals(c.Name, TargetChannelName, StringComparison.OrdinalIgnoreCase));
            if (channel != null) return channel;
        }

        _logger.LogWarning("Target channel '{ChannelName}' could not be resolved in any available guild.", TargetChannelName);
        return null;
    }
}