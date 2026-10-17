namespace WK7Bot.Testing;

using System.Reflection;
using System.Runtime.CompilerServices;
using Discord;
using Discord.Interactions;
using Discord.Net;
using Discord.WebSocket;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WK7Bot.Core.Interfaces;
using WK7Bot.Core.Utilities;
using WK7Bot.Modules;
using WK7Bot.Services;
using WK7Bot.Services.Interfaces;

/// <summary>
/// Maps the optional HTTP test API used for live command smoke tests without a Discord client.
/// </summary>
public static class TestCommandEndpoints
{
    private const ulong TestGuildId = 551130054776717323ul;
    private const ulong TestUserId = 162201162257399808ul;
    private const string TestUsername = "Dracus";

    /// <summary>
    /// Maps the <c>/test</c> endpoint group when the feature is enabled and a test API key is configured.
    /// </summary>
    /// <param name="app">The web application whose endpoint route builder should be extended.</param>
    public static void MapTestCommandEndpoints(this WebApplication app)
    {
        var testApiEnabled = app.Configuration.GetValue<bool>("test_api_enabled");
        var apiKey = app.Configuration["WK7_TEST_API_KEY"] ?? app.Configuration["test_api_key"];
        if (!testApiEnabled || string.IsNullOrWhiteSpace(apiKey))
        {
            return;
        }

        var group = app.MapGroup("/test");
        group.AddEndpointFilter(async (context, next) =>
        {
            var supplied = context.HttpContext.Request.Headers["X-WK7-Test"].FirstOrDefault();
            if (!string.Equals(supplied, apiKey, StringComparison.Ordinal))
            {
                return Results.Unauthorized();
            }

            return await next(context);
        });

        group.MapGet("/commands", () => Results.Ok(new
        {
            commands = new[]
            {
                "ping", "ha-status", "stats", "health", "help", "check-waste", "check-waste-day",
                "check-food", "seasonal-produce", "recipe", "recipe-seasonal", "recipe-generate",
                "deals", "spontan-treff", "rss-list", "rss-add", "rss-remove", "rss-dashboard",
                "rss-pause", "rss-resume", "rss-test", "rss-export", "rss-import",
                "link-steam", "link-battlenet", "link-status", "link-remove", "wishlist-add", "weather", "leaderboard",
                "watch", "unwatch", "watchlist", "config-feature", "config-list", "profile", "poll"
            }
        }));

        group.MapPost("/run/{command}", async (string command, TestRunRequest request, IServiceProvider services, CancellationToken ct) =>
        {
            request ??= new TestRunRequest();
            var result = await TestCommandRunner.RunAsync(services, command, request, TestGuildId, TestUserId, TestUsername, ct);
            return Results.Ok(result);
        });

        // The skill/docs contract uses POST /test/commands/{command}; keep the legacy /run route
        // working for callers that discovered the original path first.
        group.MapPost("/commands/{command}", async (string command, TestRunRequest request, IServiceProvider services, CancellationToken ct) =>
        {
            request ??= new TestRunRequest();
            var result = await TestCommandRunner.RunAsync(services, command, request, TestGuildId, TestUserId, TestUsername, ct);
            return Results.Ok(result);
        });
    }
}

/// <summary>
/// Request payload accepted by the HTTP command test runner.
/// </summary>
public sealed class TestRunRequest
{
    /// <summary>
    /// Gets or sets the command argument map keyed by argument name.
    /// </summary>
    public Dictionary<string, string?> Args { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets or sets the optional username used for the synthetic interaction context.
    /// </summary>
    public string? Username { get; set; }
}

/// <summary>
/// Result payload returned by the HTTP command test runner.
/// </summary>
public sealed class TestCommandResult
{
    /// <summary>
    /// Gets or sets a value indicating whether the requested command completed successfully.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Gets or sets the normalized command name.
    /// </summary>
    public string Command { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the human-readable success message returned by the runner.
    /// </summary>
    public string? Message { get; set; }

    /// <summary>
    /// Gets or sets the exception text when execution failed.
    /// </summary>
    public string? Error { get; set; }
}
