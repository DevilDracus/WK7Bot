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
        IExternalDependencyHealthChecker dependencyHealthChecker)
        : base(homeAssistantService, new CommandUsageRepository(dbContextFactory), healthTracker, dependencyHealthChecker, NullLogger<SystemModule>.Instance)
    {
    }

    protected override Task RespondAsync(string? text = null, Embed[]? embeds = null, bool isTTS = false, bool ephemeral = false, AllowedMentions? allowedMentions = null, RequestOptions? options = null, MessageComponent? components = null, Embed? embed = null, PollProperties? poll = null) =>
        Task.CompletedTask;

    protected override Task DeferAsync(bool ephemeral, RequestOptions options) =>
        Task.CompletedTask;

    protected override Task<IUserMessage> FollowupAsync(string text, Embed[] embeds, bool isTTS, bool ephemeral, AllowedMentions allowedMentions, RequestOptions options, MessageComponent components, Embed embed, PollProperties poll) =>
        Task.FromResult<IUserMessage>(null!);
}

internal sealed class TestableLeipzigWasteModule : LeipzigWasteModule
{
    public TestableLeipzigWasteModule(ILeipzigWasteService wasteService, CommandRateLimiter rateLimiter, ILogger<LeipzigWasteModule> logger)
        : base(wasteService, rateLimiter, logger) { }

    protected override Task DeferAsync(bool ephemeral, RequestOptions options) => Task.CompletedTask;
    protected override Task<IUserMessage> FollowupAsync(string text, Embed[] embeds, bool isTTS, bool ephemeral, AllowedMentions allowedMentions, RequestOptions options, MessageComponent components, Embed embed, PollProperties poll) => Task.FromResult<IUserMessage>(null!);
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

    protected override Task RespondAsync(string? text = null, Embed[]? embeds = null, bool isTTS = false, bool ephemeral = false, AllowedMentions? allowedMentions = null, RequestOptions? options = null, MessageComponent? components = null, Embed? embed = null, PollProperties? poll = null) =>
        Task.CompletedTask;
    protected override Task DeferAsync(bool ephemeral, RequestOptions options) => Task.CompletedTask;
    protected override Task<IUserMessage> FollowupAsync(string text, Embed[] embeds, bool isTTS, bool ephemeral, AllowedMentions allowedMentions, RequestOptions options, MessageComponent components, Embed embed, PollProperties poll) => Task.FromResult<IUserMessage>(null!);
}

internal sealed class TestableDealsModule : DealsModule
{
    public TestableDealsModule(
        IWishlistDealService dealService,
        ISteamService steamService,
        CommandRateLimiter rateLimiter,
        ILogger<DealsModule> logger)
        : base(dealService, steamService, rateLimiter, logger) { }

    protected override Task RespondAsync(string? text = null, Embed[]? embeds = null, bool isTTS = false, bool ephemeral = false, AllowedMentions? allowedMentions = null, RequestOptions? options = null, MessageComponent? components = null, Embed? embed = null, PollProperties? poll = null) =>
        Task.CompletedTask;
    protected override Task DeferAsync(bool ephemeral, RequestOptions options) => Task.CompletedTask;
    protected override Task<IUserMessage> FollowupAsync(string text, Embed[] embeds, bool isTTS, bool ephemeral, AllowedMentions allowedMentions, RequestOptions options, MessageComponent components, Embed embed, PollProperties poll) => Task.FromResult<IUserMessage>(null!);
}

internal sealed class TestableRssModule : RssModule
{
    public TestableRssModule(IRssRepository repository, ILogger<RssModule> logger)
        : base(repository, logger) { }

    protected override Task RespondAsync(string? text = null, Embed[]? embeds = null, bool isTTS = false, bool ephemeral = false, AllowedMentions? allowedMentions = null, RequestOptions? options = null, MessageComponent? components = null, Embed? embed = null, PollProperties? poll = null) =>
        Task.CompletedTask;
    protected override Task DeferAsync(bool ephemeral, RequestOptions options) => Task.CompletedTask;
    protected override Task<IUserMessage> FollowupAsync(string text, Embed[] embeds, bool isTTS, bool ephemeral, AllowedMentions allowedMentions, RequestOptions options, MessageComponent components, Embed embed, PollProperties poll) => Task.FromResult<IUserMessage>(null!);
}

internal sealed class TestableSpontanTreffModule : SpontanTreffModule
{
    public TestableSpontanTreffModule(ISpontanTreffRepository repository, ILogger<SpontanTreffModule> logger)
        : base(repository, logger) { }

    protected override Task DeferAsync(bool ephemeral, RequestOptions options) => Task.CompletedTask;
    protected override Task<IUserMessage> FollowupAsync(string text, Embed[] embeds, bool isTTS, bool ephemeral, AllowedMentions allowedMentions, RequestOptions options, MessageComponent components, Embed embed, PollProperties poll) => Task.FromResult<IUserMessage>(null!);
}
