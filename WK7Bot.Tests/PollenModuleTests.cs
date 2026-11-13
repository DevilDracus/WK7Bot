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

public class PollenModuleTests
{
    private sealed class TestPollenModule : PollenModule
    {
        public int DeferCount { get; private set; }

        public List<string> Followups { get; } = new();

        public List<(MessageComponent Components, MessageFlags Flags)> ComponentPosts { get; } = new();

        public TestPollenModule(IPollenService pollenService)
            : base(pollenService, NullLogger<PollenModule>.Instance)
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
            if (text != null)
            {
                Followups.Add(text);
            }

            if (components != null)
            {
                ComponentPosts.Add((components, flags));
            }

            return Task.FromResult<IUserMessage>(null!);
        }
    }

    private static TestPollenModule CreateModule(IPollenService service)
    {
        var module = new TestPollenModule(service);
        var context = DiscordTestDoubles.CreateContext();
        DiscordTestDoubles.SetUser(context, "Tester", 42);
        ((IInteractionModuleBase)module).SetContext(context);
        return module;
    }

    private static PollenForecast CreatePollenForecast()
        => new(
            "Sachsen",
            "Tiefland Sachsen",
            DateTimeOffset.UtcNow,
            new List<PollenTypeForecast>
            {
                new("Birke", new PollenLevel("2", 2, "mittlere Belastung"), new PollenLevel("1", 1, "geringe Belastung"), new PollenLevel("0", 0, "keine Belastung"))
            });

    private static UvForecast CreateUvForecast()
        => new("Leipzig", 3, 4, 5, DateTimeOffset.UtcNow);

    [Fact]
    public void Constructor_Throws_WhenDependenciesAreNull()
    {
        Assert.Throws<ArgumentNullException>(() => new PollenModule(null!, NullLogger<PollenModule>.Instance));
        Assert.Throws<ArgumentNullException>(() => new PollenModule(new Mock<IPollenService>().Object, null!));
    }

    [Fact]
    public void PollenCommand_IsExposedAsSlashCommand()
    {
        var method = typeof(PollenModule).GetMethod(nameof(PollenModule.PollenAsync))!;
        var attribute = method.GetCustomAttribute<SlashCommandAttribute>();

        Assert.NotNull(attribute);
        Assert.Equal("pollen", attribute!.Name);
        Assert.Contains("Pollenflug", attribute.Description);
    }

    [Fact]
    public async Task PollenAsync_ReportsUnavailable_WhenBothFeedsAreNull()
    {
        var service = new Mock<IPollenService>();
        service.Setup(s => s.GetPollenAsync(It.IsAny<CancellationToken>())).ReturnsAsync((PollenForecast?)null);
        service.Setup(s => s.GetUvAsync(It.IsAny<CancellationToken>())).ReturnsAsync((UvForecast?)null);
        var module = CreateModule(service.Object);

        await module.PollenAsync();

        Assert.Equal(1, module.DeferCount);
        Assert.Empty(module.ComponentPosts);
        var followup = Assert.Single(module.Followups);
        Assert.Contains("nicht erreichbar", followup);
    }

    [Fact]
    public async Task PollenAsync_PostsComponentsV2_WhenPollenFeedIsAvailable()
    {
        var service = new Mock<IPollenService>();
        service.Setup(s => s.GetPollenAsync(It.IsAny<CancellationToken>())).ReturnsAsync(CreatePollenForecast());
        service.Setup(s => s.GetUvAsync(It.IsAny<CancellationToken>())).ReturnsAsync((UvForecast?)null);
        var module = CreateModule(service.Object);

        await module.PollenAsync();

        Assert.Equal(1, module.DeferCount);
        Assert.Empty(module.Followups);
        var post = Assert.Single(module.ComponentPosts);
        Assert.Equal(MessageFlags.ComponentsV2, post.Flags);
        Assert.NotNull(post.Components);
    }

    [Fact]
    public async Task PollenAsync_PostsComponentsV2_WhenUvFeedIsAvailable()
    {
        var service = new Mock<IPollenService>();
        service.Setup(s => s.GetPollenAsync(It.IsAny<CancellationToken>())).ReturnsAsync((PollenForecast?)null);
        service.Setup(s => s.GetUvAsync(It.IsAny<CancellationToken>())).ReturnsAsync(CreateUvForecast());
        var module = CreateModule(service.Object);

        await module.PollenAsync();

        Assert.Equal(1, module.DeferCount);
        var post = Assert.Single(module.ComponentPosts);
        Assert.Equal(MessageFlags.ComponentsV2, post.Flags);
    }

    [Fact]
    public async Task PollenAsync_ReportsError_WhenServiceThrows()
    {
        var service = new Mock<IPollenService>();
        service.Setup(s => s.GetPollenAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("feed down"));
        var module = CreateModule(service.Object);

        await module.PollenAsync();

        Assert.Equal(1, module.DeferCount);
        var followup = Assert.Single(module.Followups);
        Assert.Contains("konnten nicht geladen werden", followup);
    }
}
