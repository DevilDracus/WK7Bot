using System.Net;
using System.Reflection;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using WK7Bot.Models;
using WK7Bot.Modules;
using WK7Bot.Services.Interfaces;
using Xunit;

namespace WK7Bot.Tests;

public class AchievementsModuleTests
{
    private sealed class TestAchievementsModule : AchievementsModule
    {
        public int DeferCount { get; private set; }
        public List<string> Followups { get; } = new();
        public Embed? LastEmbed { get; private set; }

        public TestAchievementsModule(ISteamService steamService, IHttpClientFactory httpClientFactory)
            : base(steamService, httpClientFactory, NullLogger<AchievementsModule>.Instance)
        {
        }

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
            if (text != null) Followups.Add(text);
            LastEmbed = embed;
            return Task.FromResult<IUserMessage>(null!);
        }
    }

    private sealed class TestHttpMessageHandler : HttpMessageHandler
    {
        private readonly HttpResponseMessage _response;

        public TestHttpMessageHandler(HttpResponseMessage response) => _response = response;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(_response);
    }

    private static IHttpClientFactory CreateFactory(HttpResponseMessage response)
    {
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(new HttpClient(new TestHttpMessageHandler(response)));
        return factory.Object;
    }

    private static TestAchievementsModule CreateModule(Mock<ISteamService> steamService, IHttpClientFactory? httpFactory = null, ulong userId = 11)
    {
        var module = new TestAchievementsModule(steamService.Object, httpFactory ?? Mock.Of<IHttpClientFactory>());
        var context = DiscordTestDoubles.CreateContext();
        DiscordTestDoubles.SetUser(context, "Tester", userId);
        ((IInteractionModuleBase)module).SetContext(context);
        return module;
    }

    [Fact]
    public void Constructor_Throws_WhenDependenciesAreNull()
    {
        var steam = new Mock<ISteamService>().Object;
        var factory = new Mock<IHttpClientFactory>().Object;
        Assert.Throws<ArgumentNullException>(() => new AchievementsModule(null!, factory, NullLogger<AchievementsModule>.Instance));
        Assert.Throws<ArgumentNullException>(() => new AchievementsModule(steam, null!, NullLogger<AchievementsModule>.Instance));
        Assert.Throws<ArgumentNullException>(() => new AchievementsModule(steam, factory, null!));
    }

    [Fact]
    public void AchievementsCommand_IsExposedAsSlashCommand()
    {
        var attr = typeof(AchievementsModule).GetMethod(nameof(AchievementsModule.AchievementsAsync))!
            .GetCustomAttribute<SlashCommandAttribute>();
        Assert.NotNull(attr);
        Assert.Equal("achievements", attr!.Name);
    }

    [Fact]
    public async Task AchievementsAsync_ReportsMissingSteamLink()
    {
        var steam = new Mock<ISteamService>();
        steam.Setup(s => s.GetSteamIdForDiscordUser(It.IsAny<string>())).Returns((string?)null);
        var module = CreateModule(steam);

        await module.AchievementsAsync("123");

        Assert.Equal(1, module.DeferCount);
        Assert.Single(module.Followups);
        Assert.Contains("keinen Steam-Account", module.Followups[0]);
        steam.Verify(s => s.GetEnrichedAchievementsAsync(It.IsAny<string>(), It.IsAny<uint>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AchievementsAsync_ReturnsEmbed_WhenNumericAppIdAndAchievementsExist()
    {
        var steam = new Mock<ISteamService>();
        steam.Setup(s => s.GetSteamIdForDiscordUser(It.IsAny<string>())).Returns("76561198000000001");
        steam.Setup(s => s.GetEnrichedAchievementsAsync("76561198000000001", 730, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SteamAchievement>
            {
                new SteamAchievement { Name = "Winner", UnlockTime = DateTime.UtcNow.AddDays(-1) },
                new SteamAchievement { Name = "Loser" }
            });
        var module = CreateModule(steam);

        await module.AchievementsAsync("730");

        Assert.Equal(1, module.DeferCount);
        Assert.Empty(module.Followups);
        Assert.NotNull(module.LastEmbed);
        Assert.Contains("730", module.LastEmbed!.Title);
        Assert.Contains("1/2", module.LastEmbed.Description);
        Assert.Contains("Winner", module.LastEmbed.Fields[0].Value);
        Assert.Contains("Loser", module.LastEmbed.Fields[1].Value);
    }

    [Fact]
    public async Task AchievementsAsync_ReportsNoAchievements()
    {
        var steam = new Mock<ISteamService>();
        steam.Setup(s => s.GetSteamIdForDiscordUser(It.IsAny<string>())).Returns("1");
        steam.Setup(s => s.GetEnrichedAchievementsAsync("1", 123, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SteamAchievement>());
        var module = CreateModule(steam);

        await module.AchievementsAsync("123");

        Assert.Single(module.Followups);
        Assert.Contains("Keine Achievements", module.Followups[0]);
    }

    [Fact]
    public async Task AchievementsAsync_ReportsGameNotFound_WhenSearchFails()
    {
        var steam = new Mock<ISteamService>();
        steam.Setup(s => s.GetSteamIdForDiscordUser(It.IsAny<string>())).Returns("1");
        var response = new HttpResponseMessage(HttpStatusCode.NotFound);
        var module = CreateModule(steam, CreateFactory(response));

        await module.AchievementsAsync("NonexistentGame");

        Assert.Equal(1, module.DeferCount);
        Assert.Single(module.Followups);
        Assert.Contains("nicht gefunden", module.Followups[0]);
    }

    [Fact]
    public async Task AchievementsAsync_PropagatesException_WhenSteamServiceThrows()
    {
        var steam = new Mock<ISteamService>();
        steam.Setup(s => s.GetSteamIdForDiscordUser(It.IsAny<string>())).Returns("1");
        steam.Setup(s => s.GetEnrichedAchievementsAsync(It.IsAny<string>(), It.IsAny<uint>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("api down"));
        var module = CreateModule(steam);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => module.AchievementsAsync("730"));
        Assert.Equal("api down", ex.Message);
    }
}
