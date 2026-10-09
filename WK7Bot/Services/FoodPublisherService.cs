using System.Globalization;

namespace WK7Bot.Services;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WK7Bot.Core.Entities;
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
    private readonly IServiceProvider _serviceProvider;
    private readonly Wk7BotOptions _options;
    private readonly ILogger<FoodPublisherService> _logger;

    private const string TargetChannelName = "🍎food";

    /// <summary>Earliest time of day the monthly produce post may go out (09:00).</summary>
    private static readonly TimeSpan MonthlyWindowOpen = TimeSpan.FromHours(9);

    /// <summary>Earliest time of day the weekly recipe post may go out (15:30, the original anchor).</summary>
    private static readonly TimeSpan WeeklyWindowOpen = new(15, 30, 0);

    /// <summary>Failed attempts are spaced five minutes apart inside the publish window.</summary>
    private static readonly TimeSpan AttemptRetryDelay = TimeSpan.FromMinutes(5);

    /// <summary>How many failed attempts a slot makes before giving up for the current period.</summary>
    private const int MaxAttemptsPerWindow = 10;

    /// <summary>Dispatch records older than this are pruned after each successful post.</summary>
    private static readonly TimeSpan DispatchRetention = TimeSpan.FromDays(90);

    /// <summary>Month and year of the last completed monthly produce slot, or <see langword="null"/> when none ran.</summary>
    private DateTime? _lastMonthlyPostDate;

    /// <summary>Date of the last completed weekly recipe slot, or <see langword="null"/> when none ran.</summary>
    private DateTime? _lastWeeklyRecipePostDate;

    /// <summary>Next allowed monthly attempt time; <see cref="DateTime.MinValue"/> means "immediately".</summary>
    private DateTime _nextMonthlyAttempt = DateTime.MinValue;

    /// <summary>Next allowed weekly attempt time; <see cref="DateTime.MinValue"/> means "immediately".</summary>
    private DateTime _nextWeeklyAttempt = DateTime.MinValue;

    /// <summary>Failed monthly attempts inside the current window.</summary>
    private int _monthlyAttempts;

    /// <summary>Failed weekly attempts inside the current slot.</summary>
    private int _weeklyAttempts;

    /// <summary>
    /// Initializes a new instance of the <see cref="FoodPublisherService"/> class.
    /// </summary>
    /// <param name="discordClient">The Discord socket client connection instance.</param>
    /// <param name="geminiFoodService">The Gemini AI food and recipe service dependency.</param>
    /// <param name="recipeSearchService">The web recipe search service used for the weekly seasonal recipe.</param>
    /// <param name="options">Application options instance.</param>
    /// <param name="serviceProvider">Root service provider used to resolve scoped database services.</param>
    /// <param name="logger">Logger instance.</param>
    /// <param name="randomSource">Randomness used to pick the seasonal search terms; defaults to <see cref="SystemRandomSource"/>.</param>
    public FoodPublisherService(
        DiscordSocketClient discordClient,
        IGeminiFoodService geminiFoodService,
        IRecipeSearchService recipeSearchService,
        IOptions<Wk7BotOptions> options,
        IServiceProvider serviceProvider,
        ILogger<FoodPublisherService> logger,
        IRandomSource? randomSource = null)
    {
        _discordClient = discordClient ?? throw new ArgumentNullException(nameof(discordClient));
        _geminiFoodService = geminiFoodService ?? throw new ArgumentNullException(nameof(geminiFoodService));
        _recipeSearchService = recipeSearchService ?? throw new ArgumentNullException(nameof(recipeSearchService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
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
                    await AttemptSlotAsync(now, FoodDispatchKinds.MonthlyProduce, "monthly produce", PublishMonthlyProduceAsync, stoppingToken);
                }

                if (ShouldPublishWeeklyRecipe(now))
                {
                    await AttemptSlotAsync(now, FoodDispatchKinds.WeeklyRecipe, "weekly recipe", PublishWeeklyRecipeAsync, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error encountered while checking or executing food background jobs.");
            }

            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        }
    }

    /// <summary>
    /// Determines whether the monthly produce post should trigger: the first day of the month, 09:00 or later,
    /// not yet posted this month, and the retry backoff elapsed. The window (instead of an exact-minute gate)
    /// means a restart or a transient failure no longer loses the post for the whole month.
    /// </summary>
    private bool ShouldPublishMonthlyProduce(DateTime now)
    {
        if (now.Day != 1) return false;
        if (now.TimeOfDay < MonthlyWindowOpen) return false;

        // Compare year AND month: a process that stays up for over a year must still post the
        // same calendar month of every later year.
        if (_lastMonthlyPostDate.HasValue
            && _lastMonthlyPostDate.Value.Year == now.Year
            && _lastMonthlyPostDate.Value.Month == now.Month)
        {
            return false;
        }

        return now >= _nextMonthlyAttempt;
    }

    /// <summary>
    /// Determines whether the weekly renal recipe should trigger: Thursday 15:30 or later, not yet posted today,
    /// and the retry backoff elapsed.
    /// </summary>
    private bool ShouldPublishWeeklyRecipe(DateTime now)
    {
        if (now.DayOfWeek != DayOfWeek.Thursday) return false;
        if (now.TimeOfDay < WeeklyWindowOpen) return false;
        if (_lastWeeklyRecipePostDate.HasValue && _lastWeeklyRecipePostDate.Value.Date == now.Date) return false;

        return now >= _nextWeeklyAttempt;
    }

    /// <summary>
    /// Runs one slot attempt with success/attempt bookkeeping so transient failures retry every
    /// five minutes instead of silently losing the slot; giving up suppresses the slot for the
    /// current period (month or week).
    /// </summary>
    /// <param name="now">The current local time; also the dispatch key for persistence.</param>
    /// <param name="kind">The dispatch kind identifying the slot.</param>
    /// <param name="slotLabel">Human-readable slot name used in log messages.</param>
    /// <param name="publish">The publish operation of this slot.</param>
    /// <param name="cancellationToken">Cancellation token for network and database operations.</param>
    private async Task AttemptSlotAsync(
        DateTime now,
        string kind,
        string slotLabel,
        Func<DateTime, CancellationToken, Task<bool>> publish,
        CancellationToken cancellationToken)
    {
        var succeeded = false;
        try
        {
            succeeded = await publish(now, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "{Slot} publication attempt failed.", slotLabel);
        }

        ref var attempts = ref _monthlyAttempts;
        ref var nextAttempt = ref _nextMonthlyAttempt;
        ref var lastPost = ref _lastMonthlyPostDate;
        if (kind == FoodDispatchKinds.WeeklyRecipe)
        {
            attempts = ref _weeklyAttempts;
            nextAttempt = ref _nextWeeklyAttempt;
            lastPost = ref _lastWeeklyRecipePostDate;
        }

        if (succeeded)
        {
            lastPost = now;
            attempts = 0;
            nextAttempt = DateTime.MinValue;
            await PersistSentAsync(kind, now, cancellationToken);
            return;
        }

        attempts++;
        if (attempts >= MaxAttemptsPerWindow)
        {
            _logger.LogError(
                "Giving up on the {Slot} post for {SlotKey:dd.MM.yyyy} after {Attempts} failed attempts.",
                slotLabel,
                now.Date,
                attempts);
            lastPost = now;
            attempts = 0;
            nextAttempt = DateTime.MinValue;
        }
        else
        {
            nextAttempt = now.Add(AttemptRetryDelay);
        }
    }

    /// <summary>
    /// Queries seasonal produce data and posts the embedded payload to the target Discord channel.
    /// </summary>
    /// <returns><see langword="true"/> when the post was delivered (or was already delivered before a restart).</returns>
    private async Task<bool> PublishMonthlyProduceAsync(DateTime now, CancellationToken cancellationToken)
    {
        if (await WasAlreadyDispatchedAsync(FoodDispatchKinds.MonthlyProduce, now, cancellationToken))
        {
            _logger.LogInformation("Monthly produce post for {Month:MM/yyyy} already dispatched before a restart; skipping.", now);
            return true;
        }

        var channel = ResolveTargetChannel();
        if (channel == null) return false;

        _logger.LogInformation("Generating monthly seasonal produce list for {Month}", now.ToString("MMMM"));

        var produce = await _geminiFoodService.GetSeasonalProduceAsync(now, cancellationToken);
        if (produce == null) return false;

        string monthName = DateTime.Today.ToString("MMMM", CultureInfo.GetCultureInfo("de-DE"));
        string fruitsFormatted = produce.Fruits.Count > 0 ? string.Join(", ", produce.Fruits) : "Keine angegeben";
        string vegetablesFormatted = produce.Vegetables.Count > 0 ? string.Join(", ", produce.Vegetables) : "Keine angegeben";
        string herbsFormatted = produce.Herbs.Count > 0 ? string.Join(", ", produce.Herbs) : "Keine angegeben";
        string nutsFormatted = produce.Nuts.Count > 0 ? string.Join(", ", produce.Nuts) : "Keine angegeben";

        var embed = new EmbedBuilder()
            .WithTitle(EmbedText.Title($"🌱 Saisonkalender: {produce.Month ?? monthName}"))
            .WithDescription(EmbedText.Description($"Übersicht der regionalen Saisonprodukte (Zentraleuropa / Leipzig-Region) für **{monthName}**."))
            .WithColor(Color.Green)
            .AddField("🍎 Obst", EmbedText.Field(fruitsFormatted), false)
            .AddField("🥕 Gemüse", EmbedText.Field(vegetablesFormatted), false)
            .AddField("🌿 Kräuter", EmbedText.Field(herbsFormatted), false)
            .AddField("🌰 Nüsse", EmbedText.Field(nutsFormatted), false)
            .WithFooter($"Sent by WK7 Bot • Regionale Saisonware")
            .WithCurrentTimestamp()
            .Build();

        await channel.SendMessageAsync(embed: embed);
        return true;
    }

    /// <summary>
    /// Finds a seasonal web recipe (seeded with randomly weighted seasonal produce, formatted via Gemini) and posts it
    /// to the target channel. When the search yields nothing, the previously used Gemini-generated recipe is posted
    /// instead so the weekly slot is never empty.
    /// </summary>
    /// <returns><see langword="true"/> when the post was delivered (or was already delivered before a restart).</returns>
    private async Task<bool> PublishWeeklyRecipeAsync(DateTime now, CancellationToken cancellationToken)
    {
        if (await WasAlreadyDispatchedAsync(FoodDispatchKinds.WeeklyRecipe, now, cancellationToken))
        {
            _logger.LogInformation("Weekly recipe post for {Date:dd.MM.yyyy} already dispatched before a restart; skipping.", now.Date);
            return true;
        }

        var channel = ResolveTargetChannel();
        if (channel == null) return false;

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
            return true;
        }

        _logger.LogWarning(
            "No seasonal web recipe found for {Date}; falling back to a Gemini-generated recipe.",
            now.ToShortDateString());

        var generated = await _geminiFoodService.GetWeeklyRenalRecipeAsync(now, cancellationToken);
        if (generated == null) return false;

        await channel.SendMessageAsync(
            embed: RecipeEmbedBuilder.BuildGenerated(generated, "Geeignet für Dialyse & Nierentransplantation"));
        return true;
    }

    /// <summary>
    /// Persists the successful post so a restart inside the same period cannot duplicate it.
    /// </summary>
    private async Task PersistSentAsync(string kind, DateTime now, CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var repository = scope.ServiceProvider.GetRequiredService<Core.Interfaces.IFoodDispatchRepository>();

            await repository.MarkSentAsync(kind, now.Date, cancellationToken);
            await repository.PruneAsync(now.Date - DispatchRetention, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist dispatch state for {Kind}; an in-memory flag guards this process until restart.", kind);
        }
    }

    /// <summary>
    /// Reads the persisted dispatch state so a restart after a successful send cannot re-post the same period.
    /// Failures fall back to the in-memory flag rather than blocking the publication.
    /// </summary>
    private async Task<bool> WasAlreadyDispatchedAsync(string kind, DateTime now, CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var repository = scope.ServiceProvider.GetRequiredService<Core.Interfaces.IFoodDispatchRepository>();
            return await repository.HasSentAsync(kind, now.Date, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not read persisted dispatch state for {Kind}; falling back to in-memory state.", kind);
            return false;
        }
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

        try
        {
            var formatted = await _geminiFoodService.FormatRecipeAsync(recipe, cancellationToken);
            if (formatted != null)
            {
                // The Gemini formatting pass returns a fresh object without seasonal metadata.
                formatted.SeasonalIngredientsUsed = seasonalTerms.ToList();
                return formatted;
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
    /// Resolves the socket text channel matching the configured target channel name inside the
    /// configured target servers: the WK7 server (or the bot test server while
    /// <c>food_service</c> is listed in <c>servers.testing_features</c>), falling back to every
    /// guild the bot is in while no server ID is configured.
    /// </summary>
    private SocketTextChannel? ResolveTargetChannel()
    {
        var targetGuildIds = AutomaticTargetResolver.Resolve(
            _options.Servers,
            "food_service",
            _discordClient.Guilds.Select(g => g.Id),
            _logger);

        foreach (var guildId in targetGuildIds)
        {
            var guild = _discordClient.GetGuild(guildId);
            if (guild == null)
            {
                _logger.LogWarning(
                    "Configured target server {GuildId} for feature 'food_service' could not be resolved; is the bot a member of it?",
                    guildId);
                continue;
            }

            var channel = guild.TextChannels.FirstOrDefault(c => string.Equals(c.Name, TargetChannelName, StringComparison.OrdinalIgnoreCase));
            if (channel != null) return channel;
        }

        _logger.LogWarning("Target channel '{ChannelName}' could not be resolved in any target server.", TargetChannelName);
        return null;
    }
}
