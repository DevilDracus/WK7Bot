namespace WK7Bot.Testing;

using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WK7Bot.Core.Interfaces;
using WK7Bot.Core.Utilities;
using WK7Bot.Infrastructure.Data;
using WK7Bot.Modules;
using WK7Bot.Options;
using WK7Bot.Services;
using WK7Bot.Services.Interfaces;

internal static class TestCommandRunner
{
    public static async Task<TestCommandResult> RunAsync(
        IServiceProvider provider,
        string command,
        TestRunRequest request,
        ulong guildId,
        ulong userId,
        string username,
        CancellationToken ct)
    {
        var normalized = (command ?? string.Empty).Trim().ToLowerInvariant();
        using var scope = provider.CreateScope();
        var services = scope.ServiceProvider;
        var loggerFactory = services.GetRequiredService<ILoggerFactory>();
        var logger = loggerFactory.CreateLogger("TestCommandRunner");

        try
        {
            var context = SocketInteractionContextFactory.Create(services, guildId, userId, username);

            // Every test-API invocation runs inside a dry-run scope: mutating handlers skip
            // persistence and AutomaticTargetResolver routes all outbound traffic to this guild.
            using var testScope = TestMode.Enter(guildId);

            var message = await ExecuteAsync(services, normalized, request.Args, context, ct);
            return new TestCommandResult
            {
                Success = true,
                Command = normalized,
                Message = message ?? "OK"
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Test endpoint failed for command {Command}", normalized.Replace("\r", string.Empty).Replace("\n", string.Empty));
            return new TestCommandResult
            {
                Success = false,
                Command = normalized,
                Error = ex.ToString()
            };
        }
    }

    private static async Task<string?> ExecuteAsync(
        IServiceProvider services,
        string command,
        Dictionary<string, string?> args,
        SocketInteractionContext context,
        CancellationToken ct)
    {
        string Arg(string key) => args.TryGetValue(key, out var value) ? value ?? string.Empty : string.Empty;

        // Some commands expose the same value under two names (slash option name and legacy alias).
        string ArgFirst(params string[] keys)
        {
            foreach (var key in keys)
            {
                var value = Arg(key);
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }

            return string.Empty;
        }

        switch (command)
        {
            case "ping":
                {
                    var module = CreateModule<TestableSystemModule>(services);
                    SetContext(module, context);
                    await module.PingCommandAsync().ConfigureAwait(false);
                    return "ping completed";
                }
            case "ha-status":
                {
                    var module = CreateModule<TestableSystemModule>(services);
                    SetContext(module, context);
                    await module.HomeAssistantStatusCommandAsync().ConfigureAwait(false);
                    return "ha-status completed";
                }
            case "stats":
                {
                    var module = CreateModule<TestableSystemModule>(services);
                    SetContext(module, context);
                    await module.StatsCommandAsync().ConfigureAwait(false);
                    return "stats completed";
                }
            case "health":
                {
                    var module = CreateModule<TestableSystemModule>(services);
                    SetContext(module, context);
                    await module.HealthCommandAsync().ConfigureAwait(false);
                    return "health completed";
                }
            case "help":
                {
                    var module = CreateModule<TestableSystemModule>(services);
                    SetContext(module, context);
                    await module.HelpCommandAsync().ConfigureAwait(false);
                    return "help completed";
                }
            case "check-waste":
            case "check-waste-day":
                {
                    var module = CreateModule<TestableLeipzigWasteModule>(services);
                    SetContext(module, context);
                    if (TryParseInt(Arg("days"), out var days))
                    {
                        await module.CheckWasteAsync(days, null).ConfigureAwait(false);
                    }
                    else if (!string.IsNullOrWhiteSpace(Arg("datum")))
                    {
                        await module.CheckWasteAsync(null, Arg("datum")).ConfigureAwait(false);
                    }
                    else
                    {
                        await module.CheckWasteAsync(null, null).ConfigureAwait(false);
                    }

                    return "check-waste completed";
                }
            case "check-food":
                {
                    var query = Arg("query");
                    if (string.IsNullOrWhiteSpace(query))
                    {
                        query = "Banane";
                    }

                    var module = CreateModule<TestableFoodModule>(services);
                    SetContext(module, context);
                    await module.CheckFoodAsync(query).ConfigureAwait(false);
                    return $"check-food completed ({query})";
                }
            case "seasonal-produce":
                {
                    var month = TryParseInt(Arg("month"), out var parsed) ? (int?)parsed : null;
                    var module = CreateModule<TestableFoodModule>(services);
                    SetContext(module, context);
                    await module.GetSeasonalProduceAsync(month).ConfigureAwait(false);
                    return "seasonal-produce completed";
                }
            case "recipe":
                {
                    var module = CreateModule<TestableFoodModule>(services);
                    SetContext(module, context);
                    await module.SearchRecipeAsync(string.IsNullOrWhiteSpace(Arg("query")) ? "Pfannkuchen" : Arg("query")).ConfigureAwait(false);
                    return "recipe completed";
                }
            case "recipe-seasonal":
                {
                    var query = string.IsNullOrWhiteSpace(Arg("query")) ? null : Arg("query");
                    var module = CreateModule<TestableFoodModule>(services);
                    SetContext(module, context);
                    await module.SearchSeasonalRecipeAsync(query).ConfigureAwait(false);
                    return "recipe-seasonal completed";
                }
            case "recipe-generate":
                {
                    var module = CreateModule<TestableFoodModule>(services);
                    SetContext(module, context);
                    await module.GenerateRecipeAsync().ConfigureAwait(false);
                    return "recipe-generate completed";
                }
            case "deals":
                {
                    var module = CreateModule<TestableDealsModule>(services);
                    SetContext(module, context);
                    await module.GetDealsAsync().ConfigureAwait(false);
                    return "deals completed";
                }
            case "spontan-treff":
                {
                    var plan = string.IsNullOrWhiteSpace(Arg("plan")) ? "Test" : Arg("plan");
                    var ort = string.IsNullOrWhiteSpace(Arg("ort")) ? "Test" : Arg("ort");
                    var minutes = TryParseInt(Arg("minuten"), out var parsed) ? parsed : 1;
                    var module = CreateModule<TestableSpontanTreffModule>(services);
                    SetContext(module, context);
                    await module.CreateAsync(plan, ort, minutes).ConfigureAwait(false);
                    return $"spontan-treff completed ({plan})";
                }
            case "rss-list":
                {
                    var module = CreateModule<TestableRssModule>(services);
                    SetContext(module, context);
                    await module.ListFeedsAsync().ConfigureAwait(false);
                    return "rss list completed";
                }
            case "rss-add":
                {
                    var name = string.IsNullOrWhiteSpace(Arg("name")) ? $"test-{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}" : Arg("name");
                    var url = string.IsNullOrWhiteSpace(Arg("url")) ? "https://feeds.bbci.co.uk/news/rss.xml" : Arg("url");
                    var module = CreateModule<TestableRssModule>(services);
                    SetContext(module, context);
                    await module.AddFeedAsync(name, url).ConfigureAwait(false);
                    return $"rss add completed ({name})";
                }
            case "rss-remove":
                {
                    var name = Arg("name");
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        throw new ArgumentException("rss-remove requires name");
                    }

                    var module = CreateModule<TestableRssModule>(services);
                    SetContext(module, context);
                    await module.RemoveFeedAsync(name).ConfigureAwait(false);
                    return $"rss remove completed ({name})";
                }
            case "rss-dashboard":
                {
                    var module = CreateModule<TestableRssModule>(services);
                    SetContext(module, context);
                    await module.PostDashboardAsync().ConfigureAwait(false);
                    return "rss dashboard completed";
                }
            case "rss-pause":
                {
                    var module = CreateModule<TestableRssModule>(services);
                    SetContext(module, context);
                    await module.PauseFeedAsync(Arg("name")).ConfigureAwait(false);
                    return "rss pause completed";
                }
            case "rss-resume":
                {
                    var module = CreateModule<TestableRssModule>(services);
                    SetContext(module, context);
                    await module.ResumeFeedAsync(Arg("name")).ConfigureAwait(false);
                    return "rss resume completed";
                }
            case "rss-test":
                {
                    var url = string.IsNullOrWhiteSpace(Arg("url")) ? "https://feeds.bbci.co.uk/news/rss.xml" : Arg("url");
                    var module = CreateModule<TestableRssModule>(services);
                    SetContext(module, context);
                    await module.TestFeedAsync(url).ConfigureAwait(false);
                    return $"rss test completed ({url})";
                }
            case "rss-export":
                {
                    var module = CreateModule<TestableRssModule>(services);
                    SetContext(module, context);
                    await module.ExportOpmlAsync().ConfigureAwait(false);
                    return "rss export completed";
                }
            case "rss-import":
                {
                    var url = Arg("url");
                    if (string.IsNullOrWhiteSpace(url))
                    {
                        throw new ArgumentException("rss-import requires url");
                    }

                    var module = CreateModule<TestableRssModule>(services);
                    SetContext(module, context);
                    await module.ImportFromUrlAsync(url).ConfigureAwait(false);
                    return $"rss import completed ({url})";
                }
            case "config-feature":
                {
                    var key = string.IsNullOrWhiteSpace(Arg("key")) ? "discord_events_enabled" : Arg("key");
                    var state = string.IsNullOrWhiteSpace(Arg("state")) ? "on" : Arg("state");
                    var module = CreateModule<TestableConfigModule>(services);
                    SetContext(module, context);
                    await module.FeatureAsync(key, state).ConfigureAwait(false);
                    return $"config feature completed ({key}={state})";
                }
            case "config-list":
                {
                    var module = CreateModule<TestableConfigModule>(services);
                    SetContext(module, context);
                    await module.ListAsync().ConfigureAwait(false);
                    return "config list completed";
                }
            case "profile":
                {
                    var module = CreateModule<TestableProfileModule>(services);
                    SetContext(module, context);
                    await module.ProfileAsync(null).ConfigureAwait(false);
                    return "profile completed";
                }
            case "pollen":
                {
                    var module = CreateModule<TestablePollenModule>(services);
                    SetContext(module, context);
                    await module.PollenAsync().ConfigureAwait(false);
                    return "pollen completed";
                }
            case "remind":
                {
                    var wann = string.IsNullOrWhiteSpace(Arg("wann")) ? "in 30m" : Arg("wann");
                    var text = string.IsNullOrWhiteSpace(Arg("text")) ? "Test-Erinnerung" : Arg("text");
                    var wiederholung = Arg("wiederholung");
                    var hier = string.Equals(Arg("hier"), "true", StringComparison.OrdinalIgnoreCase);
                    var module = CreateModule<TestableRemindModule>(services);
                    SetContext(module, context);
                    await module.RemindAsync(wann, text, wiederholung, hier).ConfigureAwait(false);
                    return $"remind completed ({wann})";
                }
            case "reminders":
                {
                    var module = CreateModule<TestableRemindModule>(services);
                    SetContext(module, context);
                    await module.ListAsync().ConfigureAwait(false);
                    return "reminders completed";
                }
            case "remind-cancel":
                {
                    var id = TryParseInt(Arg("id"), out var parsedId) ? parsedId : 1;
                    var module = CreateModule<TestableRemindModule>(services);
                    SetContext(module, context);
                    await module.CancelAsync(id).ConfigureAwait(false);
                    return $"remind-cancel completed ({id})";
                }
            case "poll":
                {
                    var frage = string.IsNullOrWhiteSpace(Arg("frage")) ? "Testumfrage" : Arg("frage");
                    var optionen = string.IsNullOrWhiteSpace(Arg("optionen")) ? "Ja | Nein" : Arg("optionen");
                    var dauer = TryParseInt(Arg("dauer_stunden"), out var parsedDuration) ? parsedDuration : 24;
                    var module = CreateModule<TestablePollModule>(services);
                    SetContext(module, context);
                    await module.PollAsync(frage, optionen, dauer, false).ConfigureAwait(false);
                    return "poll completed";
                }
            case "link-steam":
                {
                    var module = CreateModule<TestableLinkModule>(services);
                    SetContext(module, context);
                    await module.LinkSteamAsync(Arg("steam")).ConfigureAwait(false);
                    return "link-steam completed";
                }
            case "link-battlenet":
                {
                    var module = CreateModule<TestableLinkModule>(services);
                    SetContext(module, context);
                    await module.LinkBattleNetAsync(
                        Arg("refresh_token"),
                        string.IsNullOrWhiteSpace(Arg("region")) ? null : Arg("region"),
                        string.IsNullOrWhiteSpace(Arg("battle_tag")) ? null : Arg("battle_tag"));
                    return "link-battlenet completed";
                }
            case "link-status":
                {
                    var module = CreateModule<TestableLinkModule>(services);
                    SetContext(module, context);
                    await module.LinkStatusAsync().ConfigureAwait(false);
                    return "link-status completed";
                }
            case "link-remove":
                {
                    var module = CreateModule<TestableLinkModule>(services);
                    SetContext(module, context);
                    await module.LinkRemoveAsync(Arg("provider")).ConfigureAwait(false);
                    return "link-remove completed";
                }
            case "wishlist-add":
                {
                    var module = CreateModule<TestableLinkModule>(services);
                    SetContext(module, context);
                    await module.WishlistAddAsync(Arg("url")).ConfigureAwait(false);
                    return "wishlist-add completed";
                }
            case "wishlist-add-appid":
                {
                    var appId = uint.TryParse(ArgFirst("appid", "game"), out var parsedAppId) ? parsedAppId : 730u;
                    var name = string.IsNullOrWhiteSpace(Arg("name")) ? null : Arg("name");
                    var module = CreateModule<TestableLinkModule>(services);
                    SetContext(module, context);
                    await module.WishlistAddAppIdAsync(appId, name).ConfigureAwait(false);
                    return $"wishlist-add-appid completed ({appId})";
                }
            case "wishlist-remove-appid":
                {
                    var appId = uint.TryParse(ArgFirst("appid", "game"), out var parsedAppId) ? parsedAppId : 730u;
                    var module = CreateModule<TestableLinkModule>(services);
                    SetContext(module, context);
                    await module.WishlistRemoveAppIdAsync(appId).ConfigureAwait(false);
                    return $"wishlist-remove-appid completed ({appId})";
                }
            case "wishlist-list-appid":
                {
                    var module = CreateModule<TestableLinkModule>(services);
                    SetContext(module, context);
                    await module.WishlistListAppIdAsync().ConfigureAwait(false);
                    return "wishlist-list-appid completed";
                }
            case "weather":
                {
                    var module = CreateModule<TestableWeatherModule>(services);
                    SetContext(module, context);
                    await module.WeatherAsync(string.IsNullOrWhiteSpace(Arg("entity")) ? null : Arg("entity")).ConfigureAwait(false);
                    return "weather completed";
                }
            case "leaderboard":
                {
                    var module = CreateModule<TestableLeaderboardModule>(services);
                    SetContext(module, context);
                    var type = string.IsNullOrWhiteSpace(Arg("type")) ? "steam-playtime" : Arg("type");
                    var limit = TryParseInt(Arg("limit"), out var parsedLimit) ? parsedLimit : 10;
                    await module.LeaderboardAsync(type, limit).ConfigureAwait(false);
                    return "leaderboard completed";
                }
            case "watch":
                {
                    var module = CreateModule<TestableDealWatchlistModule>(services);
                    SetContext(module, context);
                    await module.WatchAsync(Arg("game"), TryParseInt(Arg("min_discount"), out var discount) ? discount : null, null).ConfigureAwait(false);
                    return "watch completed";
                }
            case "unwatch":
                {
                    var module = CreateModule<TestableDealWatchlistModule>(services);
                    SetContext(module, context);
                    await module.UnwatchAsync(Arg("game")).ConfigureAwait(false);
                    return "unwatch completed";
                }
            case "watchlist":
                {
                    var module = CreateModule<TestableDealWatchlistModule>(services);
                    SetContext(module, context);
                    await module.WatchlistAsync().ConfigureAwait(false);
                    return "watchlist completed";
                }
            case "achievements":
                {
                    var game = ArgFirst("game", "appId");
                    var module = CreateModule<TestableAchievementsModule>(services);
                    SetContext(module, context);
                    await module.AchievementsAsync(string.IsNullOrWhiteSpace(game) ? "730" : game).ConfigureAwait(false);
                    return "achievements completed";
                }
            case "birthday-set":
                {
                    var day = TryParseInt(ArgFirst("tag", "day"), out var parsedDay) ? parsedDay : 1;
                    var month = TryParseInt(ArgFirst("monat", "month"), out var parsedMonth) ? parsedMonth : 1;
                    var module = CreateModule<TestableBirthdayModule>(services);
                    SetContext(module, context);
                    await module.SetAsync(day, month).ConfigureAwait(false);
                    return $"birthday-set completed ({day:00}.{month:00}.)";
                }
            case "add-birthday":
                {
                    var day = TryParseInt(ArgFirst("tag", "day"), out var parsedDay) ? parsedDay : 1;
                    var month = TryParseInt(ArgFirst("monat", "month"), out var parsedMonth) ? parsedMonth : 1;
                    var module = CreateModule<TestableBirthdayModule>(services);
                    SetContext(module, context);
                    await module.AddBirthdayAsync(day, month).ConfigureAwait(false);
                    return $"add-birthday completed ({day:00}.{month:00}.)";
                }
            case "birthday-show":
                {
                    var module = CreateModule<TestableBirthdayModule>(services);
                    SetContext(module, context);
                    await module.ShowAsync().ConfigureAwait(false);
                    return "birthday-show completed";
                }
            case "birthday-remove":
                {
                    var module = CreateModule<TestableBirthdayModule>(services);
                    SetContext(module, context);
                    await module.RemoveAsync().ConfigureAwait(false);
                    return "birthday-remove completed";
                }
            case "gamenight-create":
                {
                    var slots = string.IsNullOrWhiteSpace(Arg("slots")) ? null : Arg("slots");
                    var title = string.IsNullOrWhiteSpace(Arg("title")) ? null : Arg("title");
                    var module = CreateModule<TestableGameNightModule>(services);
                    SetContext(module, context);
                    await module.CreateAsync(slots, title).ConfigureAwait(false);
                    return "gamenight-create completed";
                }
            case "gamenight-reschedule":
                {
                    var slot = string.IsNullOrWhiteSpace(Arg("slot")) ? "Test" : Arg("slot");
                    var gameNightId = TryParseInt(Arg("gamenight"), out var parsedGameNight) ? parsedGameNight : 0;
                    var module = CreateModule<TestableGameNightModule>(services);
                    SetContext(module, context);
                    await module.RescheduleAsync(slot, gameNightId).ConfigureAwait(false);
                    return $"gamenight-reschedule completed ({slot})";
                }
            case "gamenight-close":
                {
                    var gameNightId = TryParseInt(Arg("gamenight"), out var parsedGameNight) ? parsedGameNight : 0;
                    var module = CreateModule<TestableGameNightModule>(services);
                    SetContext(module, context);
                    await module.CloseAsync(gameNightId).ConfigureAwait(false);
                    return $"gamenight-close completed ({gameNightId})";
                }
            case "compare-prices":
                {
                    var game = ArgFirst("game", "appId");
                    var module = CreateModule<TestablePriceComparisonModule>(services);
                    SetContext(module, context);
                    await module.ComparePricesAsync(string.IsNullOrWhiteSpace(game) ? "730" : game).ConfigureAwait(false);
                    return "compare-prices completed";
                }
            case "price-history":
                {
                    var game = ArgFirst("game", "appId");
                    var module = CreateModule<TestablePriceHistoryModule>(services);
                    SetContext(module, context);
                    await module.PriceHistoryAsync(string.IsNullOrWhiteSpace(game) ? "730" : game).ConfigureAwait(false);
                    return "price-history completed";
                }
            case "release-remind":
                {
                    var game = ArgFirst("game", "appid");
                    var module = CreateModule<TestableReleaseDateReminderModule>(services);
                    SetContext(module, context);
                    await module.ReleaseRemindAsync(string.IsNullOrWhiteSpace(game) ? "730" : game).ConfigureAwait(false);
                    return "release-remind completed";
                }
            case "release-unremind":
                {
                    var appId = uint.TryParse(ArgFirst("appid", "game"), out var parsedAppId) ? parsedAppId : 730u;
                    var module = CreateModule<TestableReleaseDateReminderModule>(services);
                    SetContext(module, context);
                    await module.ReleaseUnremindAsync(appId).ConfigureAwait(false);
                    return $"release-unremind completed ({appId})";
                }
            case "release-list":
                {
                    var module = CreateModule<TestableReleaseDateReminderModule>(services);
                    SetContext(module, context);
                    await module.ReleaseListAsync().ConfigureAwait(false);
                    return "release-list completed";
                }
            case "steam-friends-digest":
                {
                    var module = CreateModule<TestableSteamFriendDigestModule>(services);
                    SetContext(module, context);
                    await module.TriggerAsync().ConfigureAwait(false);
                    return "steam-friends-digest completed";
                }
            case "quiz":
                {
                    var module = CreateModule<TestableTriviaModule>(services);
                    SetContext(module, context);
                    await module.QuizAsync().ConfigureAwait(false);
                    return "quiz completed";
                }
            case "trivia-leaderboard":
                {
                    var module = CreateModule<TestableTriviaModule>(services);
                    SetContext(module, context);
                    await module.LeaderboardAsync().ConfigureAwait(false);
                    return "trivia-leaderboard completed";
                }
            case "xp":
                {
                    var module = CreateModule<TestableXpModule>(services);
                    SetContext(module, context);
                    await module.XpAsync().ConfigureAwait(false);
                    return "xp completed";
                }
            case "xp-rank":
                {
                    var module = CreateModule<TestableXpModule>(services);
                    SetContext(module, context);
                    await module.RankAsync().ConfigureAwait(false);
                    return "xp-rank completed";
                }
            case "xp-leaderboard":
                {
                    var scope = string.IsNullOrWhiteSpace(Arg("scope")) ? "server" : Arg("scope");
                    var limit = TryParseInt(Arg("limit"), out var parsedXpLimit) ? parsedXpLimit : 10;
                    var module = CreateModule<TestableXpModule>(services);
                    SetContext(module, context);
                    await module.LeaderboardAsync(scope, limit).ConfigureAwait(false);
                    return $"xp-leaderboard completed ({scope})";
                }
            case "xp-reset":
                {
                    var module = CreateModule<TestableXpModule>(services);
                    SetContext(module, context);
                    await module.ResetAsync(context.User).ConfigureAwait(false);
                    return "xp-reset completed";
                }
            case "xp-set":
                {
                    var level = TryParseInt(Arg("level"), out var parsedLevel) ? parsedLevel : 1;
                    var module = CreateModule<TestableXpModule>(services);
                    SetContext(module, context);
                    await module.SetAsync(context.User, level).ConfigureAwait(false);
                    return $"xp-set completed (level {level})";
                }
            default:
                throw new NotSupportedException($"Unknown test command: {command}");
        }
    }

    private static T CreateModule<T>(IServiceProvider services) where T : class
    {
        return ActivatorUtilities.CreateInstance<T>(services);
    }

    /// <summary>Sets the interaction context on a module.</summary>
    /// <param name="module">The module.</param>
    /// <param name="context">The context.</param>
    private static void SetContext(object module, SocketInteractionContext context)
    {
        var method = typeof(IInteractionModuleBase).GetMethod("SetContext");
        method!.Invoke(module, new object?[] { context });
    }

    private static bool TryParseInt(string? value, out int result) =>
        int.TryParse(value, out result);

    private sealed class FakeSocketInteractionInner { }
}

internal sealed class TestableSystemModule : SystemModule
{
    public TestableSystemModule(
        IHomeAssistantService homeAssistantService,
        IDbContextFactory<BotDbContext> dbContextFactory,
        IFeatureHealthTracker healthTracker,
        IExternalDependencyHealthChecker dependencyHealthChecker,
        CommandRateLimiter rateLimiter)
        : base(homeAssistantService, new CommandUsageRepository(dbContextFactory), healthTracker, dependencyHealthChecker, rateLimiter, NullLogger<SystemModule>.Instance)
    {
    }

    protected override Task RespondAsync(string? text = null, Embed[]? embeds = null, bool isTTS = false, bool ephemeral = false, AllowedMentions? allowedMentions = null, RequestOptions? options = null, MessageComponent? components = null, Embed? embed = null, PollProperties? poll = null, MessageFlags flags = MessageFlags.None) =>
        Task.CompletedTask;

    protected override Task DeferAsync(bool ephemeral, RequestOptions options) =>
        Task.CompletedTask;

    protected override Task<IUserMessage> FollowupAsync(string text, Embed[] embeds, bool isTTS, bool ephemeral, AllowedMentions allowedMentions, RequestOptions options, MessageComponent components, Embed embed, PollProperties? poll = null, MessageFlags flags = MessageFlags.None) =>
        Task.FromResult<IUserMessage>(null!);
}

internal sealed class TestableLeipzigWasteModule : LeipzigWasteModule
{
    public TestableLeipzigWasteModule(ILeipzigWasteService wasteService, CommandRateLimiter rateLimiter, ILogger<LeipzigWasteModule> logger)
        : base(wasteService, rateLimiter, logger) { }

    protected override Task RespondAsync(string? text = null, Embed[]? embeds = null, bool isTTS = false, bool ephemeral = false, AllowedMentions? allowedMentions = null, RequestOptions? options = null, MessageComponent? components = null, Embed? embed = null, PollProperties? poll = null, MessageFlags flags = MessageFlags.None) =>
        Task.CompletedTask;
    protected override Task DeferAsync(bool ephemeral, RequestOptions options) => Task.CompletedTask;
    protected override Task<IUserMessage> FollowupAsync(string text, Embed[] embeds, bool isTTS, bool ephemeral, AllowedMentions allowedMentions, RequestOptions options, MessageComponent components, Embed embed, PollProperties? poll = null, MessageFlags flags = MessageFlags.None) => Task.FromResult<IUserMessage>(null!);
}

internal sealed class TestableFoodModule : FoodModule
{
    public TestableFoodModule(
        IGeminiFoodService geminiFoodService,
        IRecipeSearchService recipeSearchService,
        IOpenFoodFactsService openFoodFactsService,
        CommandRateLimiter rateLimiter,
        ILogger<FoodModule> logger)
        : base(geminiFoodService, recipeSearchService, openFoodFactsService, rateLimiter, logger) { }

    protected override Task RespondAsync(string? text = null, Embed[]? embeds = null, bool isTTS = false, bool ephemeral = false, AllowedMentions? allowedMentions = null, RequestOptions? options = null, MessageComponent? components = null, Embed? embed = null, PollProperties? poll = null, MessageFlags flags = MessageFlags.None) =>
        Task.CompletedTask;
    protected override Task DeferAsync(bool ephemeral, RequestOptions options) => Task.CompletedTask;
    protected override Task<IUserMessage> FollowupAsync(string text, Embed[] embeds, bool isTTS, bool ephemeral, AllowedMentions allowedMentions, RequestOptions options, MessageComponent components, Embed embed, PollProperties? poll = null, MessageFlags flags = MessageFlags.None) => Task.FromResult<IUserMessage>(null!);
}

internal sealed class TestableDealsModule : DealsModule
{
    public TestableDealsModule(
        IWishlistDealService dealService,
        ISteamService steamService,
        IAccountLinkResolver accountLinkResolver,
        CommandRateLimiter rateLimiter,
        ILogger<DealsModule> logger)
        : base(dealService, steamService, accountLinkResolver, rateLimiter, logger) { }

    protected override Task RespondAsync(string? text = null, Embed[]? embeds = null, bool isTTS = false, bool ephemeral = false, AllowedMentions? allowedMentions = null, RequestOptions? options = null, MessageComponent? components = null, Embed? embed = null, PollProperties? poll = null, MessageFlags flags = MessageFlags.None) =>
        Task.CompletedTask;
    protected override Task DeferAsync(bool ephemeral, RequestOptions options) => Task.CompletedTask;
    protected override Task<IUserMessage> FollowupAsync(string text, Embed[] embeds, bool isTTS, bool ephemeral, AllowedMentions allowedMentions, RequestOptions options, MessageComponent components, Embed embed, PollProperties? poll = null, MessageFlags flags = MessageFlags.None) => Task.FromResult<IUserMessage>(null!);
}

internal sealed class TestableRssModule : RssModule
{
    public TestableRssModule(IRssRepository repository, RssParserService parser, IOpmlService opmlService, ILogger<RssModule> logger)
        : base(repository, parser, opmlService, logger) { }

    protected override Task RespondAsync(string? text = null, Embed[]? embeds = null, bool isTTS = false, bool ephemeral = false, AllowedMentions? allowedMentions = null, RequestOptions? options = null, MessageComponent? components = null, Embed? embed = null, PollProperties? poll = null, MessageFlags flags = MessageFlags.None) =>
        Task.CompletedTask;
    protected override Task DeferAsync(bool ephemeral, RequestOptions options) => Task.CompletedTask;
    protected override Task<IUserMessage> FollowupAsync(string text, Embed[] embeds, bool isTTS, bool ephemeral, AllowedMentions allowedMentions, RequestOptions options, MessageComponent components, Embed embed, PollProperties? poll = null, MessageFlags flags = MessageFlags.None) => Task.FromResult<IUserMessage>(null!);
    protected override Task<IUserMessage> FollowupWithFileAsync(Stream fileStream, string fileName, string? text = null, Embed[]? embeds = null, bool isTTS = false, bool ephemeral = false, AllowedMentions? allowedMentions = null, MessageComponent? components = null, Embed? embed = null, RequestOptions? options = null, PollProperties? poll = null, MessageFlags flags = MessageFlags.None) => Task.FromResult<IUserMessage>(null!);
}

internal sealed class TestableConfigModule : ConfigModule
{
    public TestableConfigModule(FeatureGate featureGate, AddOnOptionsWriter optionsWriter, ILogger<ConfigModule> logger)
        : base(featureGate, optionsWriter, logger) { }

    protected override Task RespondAsync(string? text = null, Embed[]? embeds = null, bool isTTS = false, bool ephemeral = false, AllowedMentions? allowedMentions = null, RequestOptions? options = null, MessageComponent? components = null, Embed? embed = null, PollProperties? poll = null, MessageFlags flags = MessageFlags.None) =>
        Task.CompletedTask;
    protected override Task DeferAsync(bool ephemeral, RequestOptions options) => Task.CompletedTask;
    protected override Task<IUserMessage> FollowupAsync(string text, Embed[] embeds, bool isTTS, bool ephemeral, AllowedMentions allowedMentions, RequestOptions options, MessageComponent components, Embed embed, PollProperties? poll = null, MessageFlags flags = MessageFlags.None) => Task.FromResult<IUserMessage>(null!);
}

internal sealed class TestableProfileModule : ProfileModule
{
    public TestableProfileModule(IProfileService profileService, ILogger<ProfileModule> logger)
        : base(profileService, logger) { }

    protected override Task RespondAsync(string? text = null, Embed[]? embeds = null, bool isTTS = false, bool ephemeral = false, AllowedMentions? allowedMentions = null, RequestOptions? options = null, MessageComponent? components = null, Embed? embed = null, PollProperties? poll = null, MessageFlags flags = MessageFlags.None) =>
        Task.CompletedTask;
    protected override Task DeferAsync(bool ephemeral, RequestOptions options) => Task.CompletedTask;
    protected override Task<IUserMessage> FollowupAsync(string text, Embed[] embeds, bool isTTS, bool ephemeral, AllowedMentions allowedMentions, RequestOptions options, MessageComponent components, Embed embed, PollProperties? poll = null, MessageFlags flags = MessageFlags.None) => Task.FromResult<IUserMessage>(null!);
}

internal sealed class TestablePollModule : PollModule
{
    public TestablePollModule(ILogger<PollModule> logger)
        : base(logger) { }

    protected override Task RespondAsync(string? text = null, Embed[]? embeds = null, bool isTTS = false, bool ephemeral = false, AllowedMentions? allowedMentions = null, RequestOptions? options = null, MessageComponent? components = null, Embed? embed = null, PollProperties? poll = null, MessageFlags flags = MessageFlags.None) =>
        Task.CompletedTask;
    protected override Task DeferAsync(bool ephemeral, RequestOptions options) => Task.CompletedTask;
    protected override Task<IUserMessage> FollowupAsync(string text, Embed[] embeds, bool isTTS, bool ephemeral, AllowedMentions allowedMentions, RequestOptions options, MessageComponent components, Embed embed, PollProperties? poll = null, MessageFlags flags = MessageFlags.None) => Task.FromResult<IUserMessage>(null!);
}

internal sealed class TestableSpontanTreffModule : SpontanTreffModule
{
    public TestableSpontanTreffModule(ISpontanTreffRepository repository, IDiscordScheduledEventService scheduledEventService, ILogger<SpontanTreffModule> logger)
        : base(repository, scheduledEventService, logger) { }

    protected override Task DeferAsync(bool ephemeral, RequestOptions options) => Task.CompletedTask;
    protected override Task<IUserMessage> FollowupAsync(string text, Embed[] embeds, bool isTTS, bool ephemeral, AllowedMentions allowedMentions, RequestOptions options, MessageComponent components, Embed embed, PollProperties? poll = null, MessageFlags flags = MessageFlags.None) => Task.FromResult<IUserMessage>(null!);
}

internal sealed class TestableLinkModule : LinkModule
{
    public TestableLinkModule(IAccountLinkService accountLinkService, IManualWishlistRepository manualWishlistRepository, ILogger<LinkModule> logger)
        : base(accountLinkService, manualWishlistRepository, logger) { }

    protected override Task RespondAsync(string? text = null, Embed[]? embeds = null, bool isTTS = false, bool ephemeral = false, AllowedMentions? allowedMentions = null, RequestOptions? options = null, MessageComponent? components = null, Embed? embed = null, PollProperties? poll = null, MessageFlags flags = MessageFlags.None) =>
        Task.CompletedTask;
    protected override Task DeferAsync(bool ephemeral, RequestOptions options) => Task.CompletedTask;
    protected override Task<IUserMessage> FollowupAsync(string text, Embed[] embeds, bool isTTS, bool ephemeral, AllowedMentions allowedMentions, RequestOptions options, MessageComponent components, Embed embed, PollProperties? poll = null, MessageFlags flags = MessageFlags.None) => Task.FromResult<IUserMessage>(null!);
}

internal sealed class TestableWeatherModule : WeatherModule
{
    public TestableWeatherModule(IWeatherService weatherService, ILogger<WeatherModule> logger)
        : base(weatherService, logger) { }

    protected override Task RespondAsync(string? text = null, Embed[]? embeds = null, bool isTTS = false, bool ephemeral = false, AllowedMentions? allowedMentions = null, RequestOptions? options = null, MessageComponent? components = null, Embed? embed = null, PollProperties? poll = null, MessageFlags flags = MessageFlags.None) =>
        Task.CompletedTask;
    protected override Task DeferAsync(bool ephemeral, RequestOptions options) => Task.CompletedTask;
    protected override Task<IUserMessage> FollowupAsync(string text, Embed[] embeds, bool isTTS, bool ephemeral, AllowedMentions allowedMentions, RequestOptions options, MessageComponent components, Embed embed, PollProperties? poll = null, MessageFlags flags = MessageFlags.None) => Task.FromResult<IUserMessage>(null!);
}

internal sealed class TestableLeaderboardModule : LeaderboardModule
{
    public TestableLeaderboardModule(ILeaderboardService leaderboardService, ILogger<LeaderboardModule> logger)
        : base(leaderboardService, logger) { }

    protected override Task RespondAsync(string? text = null, Embed[]? embeds = null, bool isTTS = false, bool ephemeral = false, AllowedMentions? allowedMentions = null, RequestOptions? options = null, MessageComponent? components = null, Embed? embed = null, PollProperties? poll = null, MessageFlags flags = MessageFlags.None) =>
        Task.CompletedTask;
    protected override Task DeferAsync(bool ephemeral, RequestOptions options) => Task.CompletedTask;
    protected override Task<IUserMessage> FollowupAsync(string text, Embed[] embeds, bool isTTS, bool ephemeral, AllowedMentions allowedMentions, RequestOptions options, MessageComponent components, Embed embed, PollProperties? poll = null, MessageFlags flags = MessageFlags.None) => Task.FromResult<IUserMessage>(null!);
}

internal sealed class TestablePollenModule : PollenModule
{
    public TestablePollenModule(IPollenService pollenService, ILogger<PollenModule> logger)
        : base(pollenService, logger) { }

    protected override Task RespondAsync(string? text = null, Embed[]? embeds = null, bool isTTS = false, bool ephemeral = false, AllowedMentions? allowedMentions = null, RequestOptions? options = null, MessageComponent? components = null, Embed? embed = null, PollProperties? poll = null, MessageFlags flags = MessageFlags.None) =>
        Task.CompletedTask;
    protected override Task DeferAsync(bool ephemeral, RequestOptions options) => Task.CompletedTask;
    protected override Task<IUserMessage> FollowupAsync(string text, Embed[] embeds, bool isTTS, bool ephemeral, AllowedMentions allowedMentions, RequestOptions options, MessageComponent components, Embed embed, PollProperties? poll = null, MessageFlags flags = MessageFlags.None) => Task.FromResult<IUserMessage>(null!);
}

internal sealed class TestableRemindModule : RemindModule
{
    public TestableRemindModule(IReminderRepository repository, ILogger<RemindModule> logger)
        : base(repository, logger) { }

    protected override Task RespondAsync(string? text = null, Embed[]? embeds = null, bool isTTS = false, bool ephemeral = false, AllowedMentions? allowedMentions = null, RequestOptions? options = null, MessageComponent? components = null, Embed? embed = null, PollProperties? poll = null, MessageFlags flags = MessageFlags.None) =>
        Task.CompletedTask;
    protected override Task DeferAsync(bool ephemeral, RequestOptions options) => Task.CompletedTask;
    protected override Task<IUserMessage> FollowupAsync(string text, Embed[] embeds, bool isTTS, bool ephemeral, AllowedMentions allowedMentions, RequestOptions options, MessageComponent components, Embed embed, PollProperties? poll = null, MessageFlags flags = MessageFlags.None) => Task.FromResult<IUserMessage>(null!);
}

internal sealed class TestableDealWatchlistModule : DealWatchlistModule
{
    public TestableDealWatchlistModule(IDealWatchlistService watchlistService, ILogger<DealWatchlistModule> logger)
        : base(watchlistService, logger) { }

    protected override Task RespondAsync(string? text = null, Embed[]? embeds = null, bool isTTS = false, bool ephemeral = false, AllowedMentions? allowedMentions = null, RequestOptions? options = null, MessageComponent? components = null, Embed? embed = null, PollProperties? poll = null, MessageFlags flags = MessageFlags.None) =>
        Task.CompletedTask;
    protected override Task DeferAsync(bool ephemeral, RequestOptions options) => Task.CompletedTask;
    protected override Task<IUserMessage> FollowupAsync(string text, Embed[] embeds, bool isTTS, bool ephemeral, AllowedMentions allowedMentions, RequestOptions options, MessageComponent components, Embed embed, PollProperties? poll = null, MessageFlags flags = MessageFlags.None) => Task.FromResult<IUserMessage>(null!);
}

internal sealed class TestableAchievementsModule : AchievementsModule
{
    public TestableAchievementsModule(
        ISteamService steamService,
        IHttpClientFactory httpClientFactory,
        ILogger<AchievementsModule> logger)
        : base(steamService, httpClientFactory, logger) { }

    protected override Task RespondAsync(string? text = null, Embed[]? embeds = null, bool isTTS = false, bool ephemeral = false, AllowedMentions? allowedMentions = null, RequestOptions? options = null, MessageComponent? components = null, Embed? embed = null, PollProperties? poll = null, MessageFlags flags = MessageFlags.None) =>
        Task.CompletedTask;
    protected override Task DeferAsync(bool ephemeral, RequestOptions options) => Task.CompletedTask;
    protected override Task<IUserMessage> FollowupAsync(string text, Embed[] embeds, bool isTTS, bool ephemeral, AllowedMentions allowedMentions, RequestOptions options, MessageComponent components, Embed embed, PollProperties? poll = null, MessageFlags flags = MessageFlags.None) => Task.FromResult<IUserMessage>(null!);
    protected override Task<IUserMessage> FollowupWithFileAsync(Stream fileStream, string fileName, string? text = null, Embed[]? embeds = null, bool isTTS = false, bool ephemeral = false, AllowedMentions? allowedMentions = null, MessageComponent? components = null, Embed? embed = null, RequestOptions? options = null, PollProperties? poll = null, MessageFlags flags = MessageFlags.None) => Task.FromResult<IUserMessage>(null!);
}

internal sealed class TestableBirthdayModule : BirthdayModule
{
    public TestableBirthdayModule(IBirthdayRepository repository, ILogger<BirthdayModule> logger)
        : base(repository, logger) { }

    protected override Task RespondAsync(string? text = null, Embed[]? embeds = null, bool isTTS = false, bool ephemeral = false, AllowedMentions? allowedMentions = null, RequestOptions? options = null, MessageComponent? components = null, Embed? embed = null, PollProperties? poll = null, MessageFlags flags = MessageFlags.None) =>
        Task.CompletedTask;
    protected override Task DeferAsync(bool ephemeral, RequestOptions options) => Task.CompletedTask;
    protected override Task<IUserMessage> FollowupAsync(string text, Embed[] embeds, bool isTTS, bool ephemeral, AllowedMentions allowedMentions, RequestOptions options, MessageComponent components, Embed embed, PollProperties? poll = null, MessageFlags flags = MessageFlags.None) => Task.FromResult<IUserMessage>(null!);
    protected override Task<IUserMessage> FollowupWithFileAsync(Stream fileStream, string fileName, string? text = null, Embed[]? embeds = null, bool isTTS = false, bool ephemeral = false, AllowedMentions? allowedMentions = null, MessageComponent? components = null, Embed? embed = null, RequestOptions? options = null, PollProperties? poll = null, MessageFlags flags = MessageFlags.None) => Task.FromResult<IUserMessage>(null!);
}

internal sealed class TestableGameNightModule : GameNightModule
{
    public TestableGameNightModule(
        IGameNightRepository repository,
        IDiscordScheduledEventService scheduledEventService,
        IReminderRepository? reminderRepository,
        IOptions<ServersOptions>? serversOptions,
        ILogger<GameNightModule> logger)
        : base(repository, scheduledEventService, reminderRepository, serversOptions, logger) { }

    protected override Task RespondAsync(string? text = null, Embed[]? embeds = null, bool isTTS = false, bool ephemeral = false, AllowedMentions? allowedMentions = null, RequestOptions? options = null, MessageComponent? components = null, Embed? embed = null, PollProperties? poll = null, MessageFlags flags = MessageFlags.None) =>
        Task.CompletedTask;
    protected override Task DeferAsync(bool ephemeral, RequestOptions options) => Task.CompletedTask;
    protected override Task<IUserMessage> FollowupAsync(string text, Embed[] embeds, bool isTTS, bool ephemeral, AllowedMentions allowedMentions, RequestOptions options, MessageComponent components, Embed embed, PollProperties? poll = null, MessageFlags flags = MessageFlags.None) => Task.FromResult<IUserMessage>(null!);
    protected override Task<IUserMessage> FollowupWithFileAsync(Stream fileStream, string fileName, string? text = null, Embed[]? embeds = null, bool isTTS = false, bool ephemeral = false, AllowedMentions? allowedMentions = null, MessageComponent? components = null, Embed? embed = null, RequestOptions? options = null, PollProperties? poll = null, MessageFlags flags = MessageFlags.None) => Task.FromResult<IUserMessage>(null!);
}

internal sealed class TestablePriceComparisonModule : PriceComparisonModule
{
    public TestablePriceComparisonModule(
        IWishlistDealService dealService,
        ISteamStoreService steamStoreService,
        ILogger<PriceComparisonModule> logger)
        : base(dealService, steamStoreService, logger) { }

    protected override Task RespondAsync(string? text = null, Embed[]? embeds = null, bool isTTS = false, bool ephemeral = false, AllowedMentions? allowedMentions = null, RequestOptions? options = null, MessageComponent? components = null, Embed? embed = null, PollProperties? poll = null, MessageFlags flags = MessageFlags.None) =>
        Task.CompletedTask;
    protected override Task DeferAsync(bool ephemeral, RequestOptions options) => Task.CompletedTask;
    protected override Task<IUserMessage> FollowupAsync(string text, Embed[] embeds, bool isTTS, bool ephemeral, AllowedMentions allowedMentions, RequestOptions options, MessageComponent components, Embed embed, PollProperties? poll = null, MessageFlags flags = MessageFlags.None) => Task.FromResult<IUserMessage>(null!);
    protected override Task<IUserMessage> FollowupWithFileAsync(Stream fileStream, string fileName, string? text = null, Embed[]? embeds = null, bool isTTS = false, bool ephemeral = false, AllowedMentions? allowedMentions = null, MessageComponent? components = null, Embed? embed = null, RequestOptions? options = null, PollProperties? poll = null, MessageFlags flags = MessageFlags.None) => Task.FromResult<IUserMessage>(null!);
}

internal sealed class TestablePriceHistoryModule : PriceHistoryModule
{
    public TestablePriceHistoryModule(
        IPriceHistoryRepository priceHistoryRepository,
        ISteamStoreService steamStoreService,
        IWishlistDealService dealService,
        ILogger<PriceHistoryModule> logger)
        : base(priceHistoryRepository, steamStoreService, dealService, logger) { }

    protected override Task RespondAsync(string? text = null, Embed[]? embeds = null, bool isTTS = false, bool ephemeral = false, AllowedMentions? allowedMentions = null, RequestOptions? options = null, MessageComponent? components = null, Embed? embed = null, PollProperties? poll = null, MessageFlags flags = MessageFlags.None) =>
        Task.CompletedTask;
    protected override Task DeferAsync(bool ephemeral, RequestOptions options) => Task.CompletedTask;
    protected override Task<IUserMessage> FollowupAsync(string text, Embed[] embeds, bool isTTS, bool ephemeral, AllowedMentions allowedMentions, RequestOptions options, MessageComponent components, Embed embed, PollProperties? poll = null, MessageFlags flags = MessageFlags.None) => Task.FromResult<IUserMessage>(null!);
    protected override Task<IUserMessage> FollowupWithFileAsync(Stream fileStream, string fileName, string? text = null, Embed[]? embeds = null, bool isTTS = false, bool ephemeral = false, AllowedMentions? allowedMentions = null, MessageComponent? components = null, Embed? embed = null, RequestOptions? options = null, PollProperties? poll = null, MessageFlags flags = MessageFlags.None) => Task.FromResult<IUserMessage>(null!);
}

internal sealed class TestableReleaseDateReminderModule : ReleaseDateReminderModule
{
    public TestableReleaseDateReminderModule(
        IReleaseDateReminderRepository repository,
        ISteamStoreService steamStoreService,
        ILogger<ReleaseDateReminderModule> logger)
        : base(repository, steamStoreService, logger) { }

    protected override Task RespondAsync(string? text = null, Embed[]? embeds = null, bool isTTS = false, bool ephemeral = false, AllowedMentions? allowedMentions = null, RequestOptions? options = null, MessageComponent? components = null, Embed? embed = null, PollProperties? poll = null, MessageFlags flags = MessageFlags.None) =>
        Task.CompletedTask;
    protected override Task DeferAsync(bool ephemeral, RequestOptions options) => Task.CompletedTask;
    protected override Task<IUserMessage> FollowupAsync(string text, Embed[] embeds, bool isTTS, bool ephemeral, AllowedMentions allowedMentions, RequestOptions options, MessageComponent components, Embed embed, PollProperties? poll = null, MessageFlags flags = MessageFlags.None) => Task.FromResult<IUserMessage>(null!);
    protected override Task<IUserMessage> FollowupWithFileAsync(Stream fileStream, string fileName, string? text = null, Embed[]? embeds = null, bool isTTS = false, bool ephemeral = false, AllowedMentions? allowedMentions = null, MessageComponent? components = null, Embed? embed = null, RequestOptions? options = null, PollProperties? poll = null, MessageFlags flags = MessageFlags.None) => Task.FromResult<IUserMessage>(null!);
}

internal sealed class TestableSteamFriendDigestModule : SteamFriendDigestModule
{
    public TestableSteamFriendDigestModule(
        SteamFriendDigestBackgroundService backgroundService,
        ISteamService steamService,
        IOptions<Wk7BotOptions> options,
        FeatureGate featureGate,
        ILogger<SteamFriendDigestModule> logger)
        : base(backgroundService, steamService, options, featureGate, logger) { }

    protected override Task RespondAsync(string? text = null, Embed[]? embeds = null, bool isTTS = false, bool ephemeral = false, AllowedMentions? allowedMentions = null, RequestOptions? options = null, MessageComponent? components = null, Embed? embed = null, PollProperties? poll = null, MessageFlags flags = MessageFlags.None) =>
        Task.CompletedTask;
    protected override Task DeferAsync(bool ephemeral, RequestOptions options) => Task.CompletedTask;
    protected override Task<IUserMessage> FollowupAsync(string text, Embed[] embeds, bool isTTS, bool ephemeral, AllowedMentions allowedMentions, RequestOptions options, MessageComponent components, Embed embed, PollProperties? poll = null, MessageFlags flags = MessageFlags.None) => Task.FromResult<IUserMessage>(null!);
    protected override Task<IUserMessage> FollowupWithFileAsync(Stream fileStream, string fileName, string? text = null, Embed[]? embeds = null, bool isTTS = false, bool ephemeral = false, AllowedMentions? allowedMentions = null, MessageComponent? components = null, Embed? embed = null, RequestOptions? options = null, PollProperties? poll = null, MessageFlags flags = MessageFlags.None) => Task.FromResult<IUserMessage>(null!);
}

internal sealed class TestableTriviaModule : TriviaModule
{
    public TestableTriviaModule(ITriviaScoreRepository scoreRepository, ILogger<TriviaModule> logger)
        : base(scoreRepository, logger) { }

    protected override Task RespondAsync(string? text = null, Embed[]? embeds = null, bool isTTS = false, bool ephemeral = false, AllowedMentions? allowedMentions = null, RequestOptions? options = null, MessageComponent? components = null, Embed? embed = null, PollProperties? poll = null, MessageFlags flags = MessageFlags.None) =>
        Task.CompletedTask;
    protected override Task DeferAsync(bool ephemeral, RequestOptions options) => Task.CompletedTask;

    // QuizAsync stores the id of the message returned by FollowupAsync, so the usual null no-op reply
    // would throw. Hand back an inert socket message whose default id (0) is harmless.
    protected override Task<IUserMessage> FollowupAsync(string text, Embed[] embeds, bool isTTS, bool ephemeral, AllowedMentions allowedMentions, RequestOptions options, MessageComponent components, Embed embed, PollProperties? poll = null, MessageFlags flags = MessageFlags.None) =>
        Task.FromResult<IUserMessage>((SocketUserMessage)RuntimeHelpers.GetUninitializedObject(typeof(SocketUserMessage)));

    protected override Task<IUserMessage> FollowupWithFileAsync(Stream fileStream, string fileName, string? text = null, Embed[]? embeds = null, bool isTTS = false, bool ephemeral = false, AllowedMentions? allowedMentions = null, MessageComponent? components = null, Embed? embed = null, RequestOptions? options = null, PollProperties? poll = null, MessageFlags flags = MessageFlags.None) => Task.FromResult<IUserMessage>(null!);
}

internal sealed class TestableXpModule : XpModule
{
    public TestableXpModule(IXpRepository repository, IOptions<Wk7BotOptions> options, ILogger<XpModule> logger)
        : base(repository, options, logger) { }

    protected override Task RespondAsync(string? text = null, Embed[]? embeds = null, bool isTTS = false, bool ephemeral = false, AllowedMentions? allowedMentions = null, RequestOptions? options = null, MessageComponent? components = null, Embed? embed = null, PollProperties? poll = null, MessageFlags flags = MessageFlags.None) =>
        Task.CompletedTask;
    protected override Task DeferAsync(bool ephemeral, RequestOptions options) => Task.CompletedTask;
    protected override Task<IUserMessage> FollowupAsync(string text, Embed[] embeds, bool isTTS, bool ephemeral, AllowedMentions allowedMentions, RequestOptions options, MessageComponent components, Embed embed, PollProperties? poll = null, MessageFlags flags = MessageFlags.None) => Task.FromResult<IUserMessage>(null!);
    protected override Task<IUserMessage> FollowupWithFileAsync(Stream fileStream, string fileName, string? text = null, Embed[]? embeds = null, bool isTTS = false, bool ephemeral = false, AllowedMentions? allowedMentions = null, MessageComponent? components = null, Embed? embed = null, RequestOptions? options = null, PollProperties? poll = null, MessageFlags flags = MessageFlags.None) => Task.FromResult<IUserMessage>(null!);
}
