namespace WK7Bot.Services;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WK7Bot.Core.Entities;
using WK7Bot.Core.Interfaces;
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
    private readonly FeatureHealthTracker _health;
    private readonly Wk7BotOptions _options;
    private readonly ILogger<FoodPublisherService> _logger;

    private const string TargetChannelName = "🍎food";

    /// <summary>
    /// Earliest time of day the monthly produce post may go out (09:00).
    /// </summary>
    private static readonly TimeSpan MonthlyWindowOpen = TimeSpan.FromHours(9);

    /// <summary>
    /// Earliest time of day the weekly recipe post may go out (15:30, the original anchor).
    /// </summary>
    private static readonly TimeSpan WeeklyWindowOpen = new(15, 30, 0);

    /// <summary>
    /// Failed attempts are spaced five minutes apart inside the publish window.
    /// </summary>
    private static readonly TimeSpan AttemptRetryDelay = TimeSpan.FromMinutes(5);

    /// <summary>
    /// How many failed attempts a slot makes before giving up for the current period.
    /// </summary>
    private const int MaxAttemptsPerWindow = 10;

    /// <summary>
    /// Dispatch records older than this are pruned after each successful post.
    /// </summary>
    private static readonly TimeSpan DispatchRetention = TimeSpan.FromDays(90);

    /// <summary>Retry bookkeeping for the monthly produce slot.</summary>
    private readonly FoodSlotState _monthlySlot = new();

    /// <summary>Retry bookkeeping for the weekly recipe slot.</summary>
    private readonly FoodSlotState _weeklySlot = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="FoodPublisherService"/> class.
    /// </summary>
    /// <param name="discordClient">The Discord socket client connection instance.</param>
    /// <param name="geminiFoodService">The Gemini AI food and recipe service dependency.</param>
    /// <param name="recipeSearchService">The web recipe search service used for the weekly seasonal recipe.</param>
    /// <param name="options">Application options instance.</param>
    /// <param name="serviceProvider">Root service provider used to resolve scoped database services.</param>
    /// <param name="health">The feature health tracker recording the outcome of each publishing pass.</param>
    /// <param name="logger">Logger instance.</param>
    /// <param name="randomSource">Randomness used to pick the seasonal search terms; defaults to <see cref="SystemRandomSource"/>.</param>
    public FoodPublisherService(
        DiscordSocketClient discordClient,
        IGeminiFoodService geminiFoodService,
        IRecipeSearchService recipeSearchService,
        IOptions<Wk7BotOptions> options,
        IServiceProvider serviceProvider,
        FeatureHealthTracker health,
        ILogger<FoodPublisherService> logger,
        IRandomSource? randomSource = null)
    {
        _discordClient = discordClient ?? throw new ArgumentNullException(nameof(discordClient));
        _geminiFoodService = geminiFoodService ?? throw new ArgumentNullException(nameof(geminiFoodService));
        _recipeSearchService = recipeSearchService ?? throw new ArgumentNullException(nameof(recipeSearchService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _health = health ?? throw new ArgumentNullException(nameof(health));
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
                _health.RecordFailure(FeatureKeys.FoodService, ex.Message);
            }

            try
            {
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
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

        // The year is compared as well: a process that stays up for over a year must still post the
        // same calendar month of every later year.
        if (_monthlySlot.CompletedInMonth(now)) return false;

        return now >= _monthlySlot.NextAttempt;
    }

    /// <summary>
    /// Determines whether the weekly renal recipe should trigger: Thursday 15:30 or later, not yet posted today,
    /// and the retry backoff elapsed.
    /// </summary>
    private bool ShouldPublishWeeklyRecipe(DateTime now)
    {
        if (now.DayOfWeek != DayOfWeek.Thursday) return false;
        if (now.TimeOfDay < WeeklyWindowOpen) return false;
        if (_weeklySlot.LastPost?.Date == now.Date) return false;

        return now >= _weeklySlot.NextAttempt;
    }

    /// <summary>
    /// Returns the retry bookkeeping of the slot identified by its dispatch kind.
    /// </summary>
    private FoodSlotState SlotFor(string kind)
        => kind == FoodDispatchKinds.WeeklyRecipe ? _weeklySlot : _monthlySlot;

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
            _health.RecordFailure(FeatureKeys.FoodService, ex.Message);
        }

        var slot = SlotFor(kind);

        if (succeeded)
        {
            slot.Complete(now);
            await PersistSentAsync(kind, now, cancellationToken);
            _health.RecordSuccess(FeatureKeys.FoodService);
            return;
        }

        slot.Attempts++;
        if (slot.Attempts >= MaxAttemptsPerWindow)
        {
            _logger.LogError(
                "Giving up on the {Slot} post for {SlotKey:dd.MM.yyyy} after {Attempts} failed attempts.",
                slotLabel,
                now.Date,
                slot.Attempts);
            slot.Complete(now);
        }
        else
        {
            slot.NextAttempt = now.Add(AttemptRetryDelay);
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

        _logger.LogInformation("Generating monthly seasonal produce list for {Month:MM/yyyy}.", now);

        var produce = await _geminiFoodService.GetSeasonalProduceAsync(now, cancellationToken);
        if (produce == null) return false;

        var embed = SeasonalProduceEmbedBuilder.Build(
            produce,
            now.GermanMonthName(),
            "Gesendet vom WK7 Bot • Regionale Saisonware");

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
            var repository = scope.ServiceProvider.GetRequiredService<IFoodDispatchRepository>();

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
            var repository = scope.ServiceProvider.GetRequiredService<IFoodDispatchRepository>();
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
            FeatureKeys.RoutedFeatures.FoodService,
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

            var channel = guild.FindTextChannel(TargetChannelName);
            if (channel is SocketTextChannel textChannel) return textChannel;
        }

        _logger.LogWarning("Target channel '{ChannelName}' could not be resolved in any target server.", TargetChannelName);
        return null;
    }

    /// <summary>
    /// Retry bookkeeping for one publish slot: how many attempts the current period consumed, when
    /// the next attempt may run, and the timestamp of the last completed (or given-up) period.
    /// </summary>
    private sealed class FoodSlotState
    {
        /// <summary>Gets or sets the failed attempts consumed inside the current period.</summary>
        public int Attempts { get; set; }

        /// <summary>Gets or sets the next allowed attempt time; <see cref="DateTime.MinValue"/> means "immediately".</summary>
        public DateTime NextAttempt { get; set; } = DateTime.MinValue;

        /// <summary>Gets or sets the timestamp of the last completed or given-up period.</summary>
        public DateTime? LastPost { get; set; }

        /// <summary>
        /// Determines whether the monthly period containing <paramref name="now"/> was already
        /// completed. The year is compared as well so a long-running process still posts the same
        /// calendar month of every later year.
        /// </summary>
        /// <param name="now">The current local time.</param>
        /// <returns><see langword="true"/> when the month is already handled.</returns>
        public bool CompletedInMonth(DateTime now)
            => LastPost is { } last && last.Year == now.Year && last.Month == now.Month;

        /// <summary>
        /// Marks the current period as handled: it suppresses the slot until the next period and
        /// resets the attempt counter.
        /// </summary>
        /// <param name="now">The current local time.</param>
        public void Complete(DateTime now)
        {
            LastPost = now;
            Attempts = 0;
            NextAttempt = DateTime.MinValue;
        }
    }
}
