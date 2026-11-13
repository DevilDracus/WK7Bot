using System.Reflection;
using System.Runtime.CompilerServices;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using WK7Bot.Modules;
using WK7Bot.Services.Interfaces;
using Xunit;

namespace WK7Bot.Tests;

public class WeatherModuleTests
{
    private sealed class TestModule : WeatherModule
    {
        public int DeferCount { get; private set; }
        public List<string> Followups { get; } = new();

        public TestModule(IWeatherService weatherService)
            : base(weatherService, NullLogger<WeatherModule>.Instance) { }

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

    private static TestModule CreateModule(Mock<IWeatherService> service)
    {
        var module = new TestModule(service.Object);
        var context = DiscordTestDoubles.CreateContext();
        DiscordTestDoubles.SetUser(context, "tester");
        ((IInteractionModuleBase)module).SetContext(context);
        return module;
    }

    [Fact]
    public async Task WeatherAsync_Snapshot_ReturnsWeather()
    {
        var service = new Mock<IWeatherService>();
        service.Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WeatherSnapshot(
                "weather.home",
                "Home",
                "sunny",
                22,
                45,
                10,
                1012,
                new List<WeatherForecastDay>()));

        var module = CreateModule(service);

        await module.WeatherAsync();

        Assert.Equal(1, module.DeferCount);
        Assert.Single(module.Followups);
    }

    [Fact]
    public async Task WeatherAsync_NullSnapshot_ReportsError()
    {
        var service = new Mock<IWeatherService>();
        service.Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((WeatherSnapshot?)null);

        var module = CreateModule(service);

        await module.WeatherAsync();

        Assert.Contains("Wetterdaten konnten nicht geladen", string.Join(" ", module.Followups));
    }

    [Fact]
    public async Task WeatherAsync_ServiceThrows_ReportsError()
    {
        var service = new Mock<IWeatherService>();
        service.Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("ha"));

        var module = CreateModule(service);

        await module.WeatherAsync();

        Assert.Contains("Wetter konnte nicht geladen", string.Join(" ", module.Followups));
    }

    [Fact]
    public void Command_IsSlashCommand()
    {
        var attribute = typeof(WeatherModule).GetMethod(nameof(WeatherModule.WeatherAsync))!
            .GetCustomAttribute<SlashCommandAttribute>();

        Assert.NotNull(attribute);
        Assert.Equal("weather", attribute!.Name);
    }
}
