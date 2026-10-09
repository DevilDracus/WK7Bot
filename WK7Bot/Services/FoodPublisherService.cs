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

    private DateTime? _lastMonthlyPostDate;
    private DateTime? _lastWeeklyRecipePostDate;
    private DateTime _nextMonthlyAttempt = DateTime.MinValue;
    private DateTime _nextWeeklyAttempt = DateTime.MinValue;
    private int _monthlyAttempts;
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
                    await AttemptMonthlyProduceAsync(now, stoppingToken);
                }

                if (ShouldPublishWeeklyRecipe(now))
                {
                    await AttemptWeeklyRecipeAsync(now, stoppingToken);
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
        if (_lastMonthlyPostDate.HasValue && _lastMonthlyPostDate.Value.Month == now.Month) return false;

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
    /// Runs one monthly produce attempt with success/attempt bookkeeping so transient failures retry
    /// every five minutes instead of silently losing the post until next month.
    /// </summary>
    private async Task AttemptMonthlyProduceAsync(DateTime now, CancellationToken cancellationToken)
    {
        var succeeded = false;
        try
        {
            succeeded = await PublishMonthlyProduceAsync(now, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Monthly produce publication attempt failed.");
        }

        if (succeeded)
        {
            _lastMonthlyPostDate = now;
            _monthlyAttempts = 0;
            _nextMonthlyAttempt = DateTime.MinValue;
            await PersistSentAsync(FoodDispatchKinds.MonthlyProduce, now, cancellationToken);
            return;
        }

        _monthlyAttempts++;
        if (_monthlyAttempts >= MaxAttemptsPerWindow)
        {
            _logger.LogError(
                "Giving up on the monthly produce post for {Month:MM/yyyy} after {Attempts} failed attempts.",
                now,
                _monthlyAttempts);
            _lastMonthlyPostDate = now;
            _monthlyAttempts = 0;
            _nextMonthlyAttempt = DateTime.MinValue;
        }
        else
        {
            _nextMonthlyAttempt = now.Add(AttemptRetryDelay);
        }
    }

    /// <summary>
    /// Runs one weekly recipe attempt with success/attempt bookkeeping so transient failures retry
    /// every five minutes instead of silently losing the slot for the week.
    /// </summary>
    private async Task AttemptWeeklyRecipeAsync(DateTime now, CancellationToken cancellationToken)
    {
        var succeeded = false;
        try
        {
            succeeded = await PublishWeeklyRecipeAsync(now, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Weekly recipe publication attempt failed.");
        }

        if (succeeded)
        {
            _lastWeeklyRecipePostDate = now;
            _weeklyAttempts = 0;
            _nextWeeklyAttempt = DateTime.MinValue;
            await PersistSentAsync(FoodDispatchKinds.WeeklyRecipe, now, cancellationToken);
            return;
        }

        _weeklyAttempts++;
        if (_weeklyAttempts >= MaxAttemptsPerWindow)
        {
            _logger.LogError(
                "Giving up on the weekly recipe post for {Date:dd.MM.yyyy} after {Attempts} failed attempts.",
                now.Date,
                _weeklyAttempts);
            _lastWeeklyRecipePostDate = now;
            _weeklyAttempts = 0;
            _nextWeeklyAttempt = DateTime.MinValue;
        }
        else
        {
            _nextWeeklyAttempt = now.Add(AttemptRetryDelay);
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
            embed: RecipeEmbedBuilder.BuildGenerated(generated, "Tailored for Dialysis & Kidney Transplant Safety"));
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
