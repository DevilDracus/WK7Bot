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
using WK7Bot.Infrastructure.Data;
using WK7Bot.Core.Interfaces;
using WK7Bot.Core.Utilities;
using WK7Bot.Modules;
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
            logger.LogError(ex, "Test endpoint failed for command {Command}", command);
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

        switch (command)
        {
            case "ping":
                {
                    var module = CreateModule<TestableSystemModule>(services);
                    SetContext(module, context);
                    await module.PingCommandAsync();
                    return "ping completed";
                }
            case "ha-status":
                {
                    var module = CreateModule<TestableSystemModule>(services);
                    SetContext(module, context);
                    await module.HomeAssistantStatusCommandAsync();
                    return "ha-status completed";
                }
            case "stats":
                {
                    var module = CreateModule<TestableSystemModule>(services);
                    SetContext(module, context);
                    await module.StatsCommandAsync();
                    return "stats completed";
                }
            case "health":
                {
                    var module = CreateModule<TestableSystemModule>(services);
                    SetContext(module, context);
                    await module.HealthCommandAsync();
                    return "health completed";
                }
            case "help":
                {
                    var module = CreateModule<TestableSystemModule>(services);
                    SetContext(module, context);
                    await module.HelpCommandAsync();
                    return "help completed";
                }
            case "check-waste":
            case "check-waste-day":
                {
                    var module = CreateModule<TestableLeipzigWasteModule>(services);
                    SetContext(module, context);
                    if (TryParseInt(Arg("days"), out var days))
                    {
                        await module.CheckWasteAsync(days, null);
                    }
                    else if (!string.IsNullOrWhiteSpace(Arg("datum")))
                    {
                        await module.CheckWasteAsync(null, Arg("datum"));
                    }
                    else
                    {
                        await module.CheckWasteAsync(null, null);
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
                    await module.CheckFoodAsync(query);
                    return $"check-food completed ({query})";
                }
            case "seasonal-produce":
                {
                    var month = TryParseInt(Arg("month"), out var parsed) ? (int?)parsed : null;
                    var module = CreateModule<TestableFoodModule>(services);
                    SetContext(module, context);
                    await module.GetSeasonalProduceAsync(month);
                    return "seasonal-produce completed";
                }
            case "recipe":
                {
                    var module = CreateModule<TestableFoodModule>(services);
                    SetContext(module, context);
                    await module.SearchRecipeAsync(string.IsNullOrWhiteSpace(Arg("query")) ? "Pfannkuchen" : Arg("query"));
                    return "recipe completed";
                }
            case "recipe-seasonal":
                {
                    var query = string.IsNullOrWhiteSpace(Arg("query")) ? null : Arg("query");
                    var module = CreateModule<TestableFoodModule>(services);
                    SetContext(module, context);
                    await module.SearchSeasonalRecipeAsync(query);
                    return "recipe-seasonal completed";
                }
            case "recipe-generate":
                {
                    var module = CreateModule<TestableFoodModule>(services);
                    SetContext(module, context);
                    await module.GenerateRecipeAsync();
                    return "recipe-generate completed";
                }
            case "deals":
                {
                    var module = CreateModule<TestableDealsModule>(services);
                    SetContext(module, context);
                    await module.GetDealsAsync();
                    return "deals completed";
                }
            case "spontan-treff":
                {
                    var plan = string.IsNullOrWhiteSpace(Arg("plan")) ? "Test" : Arg("plan");
                    var ort = string.IsNullOrWhiteSpace(Arg("ort")) ? "Test" : Arg("ort");
                    var minutes = TryParseInt(Arg("minuten"), out var parsed) ? parsed : 1;
                    var module = CreateModule<TestableSpontanTreffModule>(services);
                    SetContext(module, context);
                    await module.CreateAsync(plan, ort, minutes);
                    return $"spontan-treff completed ({plan})";
                }
            case "rss-list":
                {
                    var module = CreateModule<TestableRssModule>(services);
                    SetContext(module, context);
                    await module.ListFeedsAsync();
                    return "rss list completed";
                }
            case "rss-add":
                {
                    var name = string.IsNullOrWhiteSpace(Arg("name")) ? $"test-{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}" : Arg("name");
                    var url = string.IsNullOrWhiteSpace(Arg("url")) ? "https://feeds.bbci.co.uk/news/rss.xml" : Arg("url");
                    var module = CreateModule<TestableRssModule>(services);
                    SetContext(module, context);
                    await module.AddFeedAsync(name, url);
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
                    await module.RemoveFeedAsync(name);
                    return $"rss remove completed ({name})";
                }
            case "rss-dashboard":
                {
                    var module = CreateModule<TestableRssModule>(services);
                    SetContext(module, context);
                    await module.PostDashboardAsync();
                    return "rss dashboard completed";
                }
            case "rss-pause":
                {
                    var module = CreateModule<TestableRssModule>(services);
                    SetContext(module, context);
                    await module.PauseFeedAsync(Arg("name"));
                    return "rss pause completed";
                }
            case "rss-resume":
                {
                    var module = CreateModule<TestableRssModule>(services);
                    SetContext(module, context);
                    await module.ResumeFeedAsync(Arg("name"));
                    return "rss resume completed";
                }
            case "rss-test":
                {
                    var url = string.IsNullOrWhiteSpace(Arg("url")) ? "https://feeds.bbci.co.uk/news/rss.xml" : Arg("url");
                    var module = CreateModule<TestableRssModule>(services);
                    SetContext(module, context);
                    await module.TestFeedAsync(url);
                    return $"rss test completed ({url})";
                }
            case "rss-export":
                {
                    var module = CreateModule<TestableRssModule>(services);
                    SetContext(module, context);
                    await module.ExportOpmlAsync();
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
                    await module.ImportFromUrlAsync(url);
                    return $"rss import completed ({url})";
                }
            case "config-feature":
                {
                    var key = string.IsNullOrWhiteSpace(Arg("key")) ? "discord_events_enabled" : Arg("key");
                    var state = string.IsNullOrWhiteSpace(Arg("state")) ? "on" : Arg("state");
                    var module = CreateModule<TestableConfigModule>(services);
                    SetContext(module, context);
                    await module.FeatureAsync(key, state);
                    return $"config feature completed ({key}={state})";
                }
            case "config-list":
                {
                    var module = CreateModule<TestableConfigModule>(services);
                    SetContext(module, context);
                    await module.ListAsync();
                    return "config list completed";
                }
            case "profile":
                {
                    var module = CreateModule<TestableProfileModule>(services);
                    SetContext(module, context);
                    await module.ProfileAsync(null);
                    return "profile completed";
                }
            case "pollen":
                {
                    var module = CreateModule<TestablePollenModule>(services);
                    SetContext(module, context);
                    await module.PollenAsync();
                    return "pollen completed";
                }
            case "remind":
                {
                    var wann = string.IsNullOrWhiteSpace(Arg("wann")) ? "in 30m" : Arg("wann");
                    var text = string.IsNullOrWhiteSpace(Arg("text")) ? "Test-Erinnerung" : Arg("text");
                    var hier = string.Equals(Arg("hier"), "true", StringComparison.OrdinalIgnoreCase);
                    var module = CreateModule<TestableRemindModule>(services);
                    SetContext(module, context);
                    await module.RemindAsync(wann, text, hier);
                    return $"remind completed ({wann})";
                }
            case "reminders":
                {
                    var module = CreateModule<TestableRemindModule>(services);
                    SetContext(module, context);
                    await module.ListAsync();
                    return "reminders completed";
                }
            case "remind-cancel":
                {
                    var id = TryParseInt(Arg("id"), out var parsedId) ? parsedId : 1;
                    var module = CreateModule<TestableRemindModule>(services);
                    SetContext(module, context);
                    await module.CancelAsync(id);
                    return $"remind-cancel completed ({id})";
                }
            case "poll":
                {
                    var frage = string.IsNullOrWhiteSpace(Arg("frage")) ? "Testumfrage" : Arg("frage");
                    var optionen = string.IsNullOrWhiteSpace(Arg("optionen")) ? "Ja | Nein" : Arg("optionen");
                    var dauer = TryParseInt(Arg("dauer_stunden"), out var parsedDuration) ? parsedDuration : 24;
                    var module = CreateModule<TestablePollModule>(services);
                    SetContext(module, context);
                    await module.PollAsync(frage, optionen, dauer, false);
                    return "poll completed";
                }
            case "link-steam":
                {
                    var module = CreateModule<TestableLinkModule>(services);
                    SetContext(module, context);
                    await module.LinkSteamAsync(Arg("steam"));
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
                    await module.LinkStatusAsync();
                    return "link-status completed";
                }
            case "link-remove":
                {
                    var module = CreateModule<TestableLinkModule>(services);
                    SetContext(module, context);
                    await module.LinkRemoveAsync(Arg("provider"));
                    return "link-remove completed";
                }
            case "wishlist-add":
                {
                    var module = CreateModule<TestableLinkModule>(services);
                    SetContext(module, context);
                    await module.WishlistAddAsync(Arg("url"));
                    return "wishlist-add completed";
                }
            case "weather":
                {
                    var module = CreateModule<TestableWeatherModule>(services);
                    SetContext(module, context);
                    await module.WeatherAsync(string.IsNullOrWhiteSpace(Arg("entity")) ? null : Arg("entity"));
                    return "weather completed";
                }
            case "leaderboard":
                {
                    var module = CreateModule<TestableLeaderboardModule>(services);
                    SetContext(module, context);
                    var type = string.IsNullOrWhiteSpace(Arg("type")) ? "steam-playtime" : Arg("type");
                    var limit = TryParseInt(Arg("limit"), out var parsedLimit) ? parsedLimit : 10;
                    await module.LeaderboardAsync(type, limit);
                    return "leaderboard completed";
                }
            case "watch":
                {
                    var module = CreateModule<TestableDealWatchlistModule>(services);
                    SetContext(module, context);
                    await module.WatchAsync(Arg("game"), TryParseInt(Arg("min_discount"), out var discount) ? discount : null, null);
                    return "watch completed";
                }
            case "unwatch":
                {
                    var module = CreateModule<TestableDealWatchlistModule>(services);
                    SetContext(module, context);
                    await module.UnwatchAsync(Arg("game"));
                    return "unwatch completed";
                }
            case "watchlist":
                {
                    var module = CreateModule<TestableDealWatchlistModule>(services);
                    SetContext(module, context);
                    await module.WatchlistAsync();
                    return "watchlist completed";
                }
            default:
                throw new NotSupportedException($"Unknown test command: {command}");
        }
    }

    private static T CreateModule<T>(IServiceProvider services) where T : class
    {
        return ActivatorUtilities.CreateInstance<T>(services);
    }

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
        FeatureHealthTracker healthTracker,
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
    public TestableLinkModule(IAccountLinkService accountLinkService, ILogger<LinkModule> logger)
        : base(accountLinkService, logger) { }

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
