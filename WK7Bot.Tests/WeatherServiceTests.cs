using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using WK7Bot.Options;
using WK7Bot.Services;
using WK7Bot.Services.Interfaces;
using Xunit;

namespace WK7Bot.Tests;

public class WeatherServiceTests
{
    private static WeatherService CreateService(string? payload, string entity = "weather.home")
    {
        var ha = new Mock<IHomeAssistantService>();
        ha.Setup(s => s.GetApiStateAsync($"states/{entity}", It.IsAny<CancellationToken>())).ReturnsAsync(payload);
        return new WeatherService(ha.Object, Microsoft.Extensions.Options.Options.Create(new Wk7BotOptions { WeatherEntity = entity }), NullLogger<WeatherService>.Instance);
    }

    [Fact]
    public async Task GetAsync_ParsesStateAndAttributes()
    {
        var json = """
        {
          "entity_id": "weather.home",
          "state": "sunny",
          "attributes": {
            "friendly_name": "Zuhause",
            "temperature": 21.5,
            "humidity": 48,
            "wind_speed": 12.3,
            "pressure": 1013
          }
        }
        """;

        var snapshot = await CreateService(json).GetAsync();

        Assert.NotNull(snapshot);
        Assert.Equal("weather.home", snapshot!.EntityId);
        Assert.Equal("Zuhause", snapshot.FriendlyName);
        Assert.Equal("sunny", snapshot.Condition);
        Assert.Equal(21.5, snapshot.Temperature);
        Assert.Equal(48, snapshot.Humidity);
    }

    [Fact]
    public async Task GetAsync_ReturnsNull_WhenEntityUnavailable()
    {
        var snapshot = await CreateService(null).GetAsync();

        Assert.Null(snapshot);
    }

    [Fact]
    public void Constructor_Throws_WhenDependenciesAreNull()
    {
        Assert.Throws<ArgumentNullException>(() => new WeatherService(null!, Microsoft.Extensions.Options.Options.Create(new Wk7BotOptions()), NullLogger<WeatherService>.Instance));
    }
}
