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

public static class TestCommandEndpoints
{
    private const ulong TestGuildId = 551130054776717323ul;
    private const ulong TestUserId = 162201162257399808ul;
    private const string TestUsername = "Dracus";

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
                "deals", "spontan-treff", "rss-list", "rss-add", "rss-remove", "rss-dashboard"
            }
        }));

        group.MapPost("/run/{command}", async (string command, TestRunRequest request, IServiceProvider services, CancellationToken ct) =>
        {
            request ??= new TestRunRequest();
            var result = await TestCommandRunner.RunAsync(services, command, request, TestGuildId, TestUserId, TestUsername, ct);
            return Results.Ok(result);
        });
    }
}

public sealed class TestRunRequest
{
    public Dictionary<string, string?> Args { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string? Username { get; set; }
}

public sealed class TestCommandResult
{
    public bool Success { get; set; }
    public string Command { get; set; } = string.Empty;
    public string? Message { get; set; }
    public string? Error { get; set; }
}
