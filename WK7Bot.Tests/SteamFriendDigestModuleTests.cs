using Microsoft.Extensions.Options;
using System.Reflection;
using System.Runtime.CompilerServices;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.Extensions.Logging.Abstractions;
using Options = Microsoft.Extensions.Options.Options;
using Moq;
using WK7Bot.Core.Utilities;
using WK7Bot.Models;
using WK7Bot.Modules;
using WK7Bot.Services;
using WK7Bot.Services.Interfaces;
using Xunit;

namespace WK7Bot.Tests;

public class SteamFriendDigestModuleTests
{
    private sealed class TestModule : SteamFriendDigestModule
    {
        public int DeferCount { get; private set; }
        public List<string> Followups { get; } = new();

        public TestModule(
            SteamFriendDigestBackgroundService backgroundService,
            ISteamService steamService,
            IOptions<Wk7BotOptions> options,
            FeatureGate featureGate)
            : base(backgroundService, steamService, options, featureGate, NullLogger<SteamFriendDigestModule>.Instance) { }

        protected override Task DeferAsync(bool ephemeral, RequestOptions options)
        {
            DeferCount++;
            return Task.CompletedTask;
        }

        protected override Task<IUserMessage> FollowupAsync(
            string text,
            Embed[] embeds,
            bool isTTS,
            bool ephemeral,
            AllowedMentions allowedMentions,
            RequestOptions options,
            MessageComponent components,
            Embed embed,
            PollProperties? poll = null,
            MessageFlags flags = MessageFlags.None)
        {
            Followups.Add(text ?? string.Empty);
            return Task.FromResult<IUserMessage>(null!);
        }
    }

    private static Wk7BotOptions CreateOptions(bool enabled = true, string? apiKey = "key", params DiscordSteamMappingOptions[] mappings)
    {
        var options = new Wk7BotOptions
        {
            SteamApiKey = apiKey,
            DiscordSteamMappings = mappings.ToList()
        };
        typeof(Wk7BotOptions).GetProperty("Features")!.SetValue(options, new FeatureOptions
        {
            SteamFriendDigestEnabled = enabled
        });
        return options;
    }

    private static TestModule CreateModule(
        Wk7BotOptions? options = null,
        ISteamService? steamService = null,
        SteamFriendDigestBackgroundService? backgroundService = null)
    {
        var opts = options ?? CreateOptions();
        var featureGate = new FeatureGate(Microsoft.Extensions.Options.Options.Create(opts));
        var svc = steamService ?? new Mock<ISteamService>().Object;
        var bg = backgroundService ?? new SteamFriendDigestBackgroundService(
            DiscordTestDoubles.CreateClient(),
            svc,
            new FeatureHealthTracker(),
            featureGate,
            Microsoft.Extensions.Options.Options.Create(opts),
            NullLogger<SteamFriendDigestBackgroundService>.Instance);

        var module = new TestModule(bg, svc, Microsoft.Extensions.Options.Options.Create(opts), featureGate);
        var context = DiscordTestDoubles.CreateContext();
        DiscordTestDoubles.SetUser(context, "tester");
        ((IInteractionModuleBase)module).SetContext(context);
        return module;
    }

    [Fact]
    public async Task TriggerAsync_Disabled_ReportsDisabled()
    {
        var options = CreateOptions(enabled: false);
        var module = CreateModule(options);

        await module.TriggerAsync();

        Assert.Equal(1, module.DeferCount);
        Assert.Contains("deaktiviert", string.Join(" ", module.Followups));
    }

    [Fact]
    public async Task TriggerAsync_NoApiKey_ReportsError()
    {
        var options = CreateOptions(apiKey: "");
        var module = CreateModule(options);

        await module.TriggerAsync();

        Assert.Contains("Kein Steam-API-Key", string.Join(" ", module.Followups));
    }

    [Fact]
    public async Task TriggerAsync_NoMappings_ReportsError()
    {
        var options = CreateOptions(mappings: Array.Empty<DiscordSteamMappingOptions>());
        var module = CreateModule(options);

        await module.TriggerAsync();

        Assert.Contains("Keine Discord-Steam-Verknüpfungen", string.Join(" ", module.Followups));
    }

    [Fact]
    public async Task TriggerAsync_Success_ReportsSuccess()
    {
        var steamService = new Mock<ISteamService>();
        steamService.Setup(s => s.GetUserActivitiesAsync(
                It.IsAny<List<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SteamUserActivity>());
        var options = CreateOptions(mappings: new DiscordSteamMappingOptions { SteamId = "1" });
        var module = CreateModule(options, steamService.Object);

        await module.TriggerAsync();

        Assert.Contains("ausgelöst", string.Join(" ", module.Followups));
    }

    [Fact]
    public async Task TriggerAsync_BackgroundServiceThrows_ReportsError()
    {
        var steamService = new Mock<ISteamService>();
        steamService.Setup(s => s.GetUserActivitiesAsync(
                It.IsAny<List<string>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("steam"));
        var options = CreateOptions(mappings: new DiscordSteamMappingOptions { SteamId = "1" });
        var module = CreateModule(options, steamService.Object);

        await module.TriggerAsync();

        Assert.Contains("konnte nicht ausgelöst", string.Join(" ", module.Followups));
    }

    [Fact]
    public void Command_IsSlashCommand()
    {
        var attribute = typeof(SteamFriendDigestModule).GetMethod(nameof(SteamFriendDigestModule.TriggerAsync))!
            .GetCustomAttribute<SlashCommandAttribute>();

        Assert.NotNull(attribute);
        Assert.Equal("steam-friends-digest", attribute!.Name);
    }
}
