using System.Net;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;
using WK7Bot.Core.Utilities;
using WK7Bot.Models.Steam;
using WK7Bot.Options;
using WK7Bot.Services;
using Xunit;
using OptionsFactory = Microsoft.Extensions.Options.Options;

namespace WK7Bot.Tests;

public class SteamServiceActivityTests
{
    private static HttpClient CreateClient(params (string UrlPattern, string Response)[] responses)
    {
        var handler = new Mock<HttpMessageHandler>();
        handler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync((HttpRequestMessage req, CancellationToken _) =>
            {
                var url = req.RequestUri?.ToString() ?? string.Empty;
                foreach (var (pattern, response) in responses)
                {
                    if (url.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                    {
                        return new HttpResponseMessage(HttpStatusCode.OK)
                        {
                            Content = new StringContent(response)
                        };
                    }
                }

                return new HttpResponseMessage(HttpStatusCode.NotFound);
            });

        return new HttpClient(handler.Object)
        {
            BaseAddress = new Uri("https://api.steampowered.com/")
        };
    }

    private static SteamService CreateService(HttpClient client)
    {
        var options = OptionsFactory.Create(new Wk7BotOptions { SteamApiKey = "test-key" });
        return new SteamService(client, new MemoryCache(new MemoryCacheOptions()), options, NullLogger<SteamService>.Instance, new MetricsRegistry());
    }

    [Fact]
    public async Task GetUserActivitiesAsync_ReturnsCurrentGameAndRecentGames()
    {
        var summaries = @"{ ""response"": { ""players"": [
            { ""steamid"":""100"", ""personaname"":""Alice"", ""personastate"":1, ""gameextrainfo"":""Game One"", ""gameid"":""123"" }
        ] }}";

        var recent = @"{ ""response"": { ""games"": [
            { ""appid"":123, ""name"":""Game One"", ""playtime_2weeks"":120, ""playtime_forever"":500 }
        ] }}";

        var service = CreateService(CreateClient(
            ("GetPlayerSummaries", summaries),
            ("GetRecentlyPlayedGames", recent)));

        var result = await service.GetUserActivitiesAsync(new[] { "100" });

        var activity = Assert.Single(result);
        Assert.Equal("100", activity.SteamId);
        Assert.Equal("Alice", activity.PersonaName);
        Assert.Equal("Game One", activity.CurrentGameTitle);
        Assert.Equal(123u, activity.CurrentGameAppId);
        Assert.Single(activity.RecentGames);
        Assert.Equal(120, activity.RecentGames[0].PlaytimeTwoWeeksMinutes);
    }

    [Fact]
    public async Task GetUserActivitiesAsync_ReturnsEmpty_WhenNoApiKey()
    {
        var options = OptionsFactory.Create(new Wk7BotOptions());
        var service = new SteamService(
            CreateClient(),
            new MemoryCache(new MemoryCacheOptions()),
            options,
            NullLogger<SteamService>.Instance,
            new MetricsRegistry());

        var result = await service.GetUserActivitiesAsync(new[] { "100" });

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetUserActivitiesAsync_ReturnsEmpty_WhenSteamIdsEmpty()
    {
        var service = CreateService(CreateClient());

        var result = await service.GetUserActivitiesAsync(Array.Empty<string>());

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetUserActivitiesAsync_HandlesMissingRecentGames()
    {
        var summaries = @"{ ""response"": { ""players"": [
            { ""steamid"":""200"", ""personaname"":""Bob"", ""personastate"":0 }
        ] }}";

        var recent = @"{ ""response"": {}}";

        var service = CreateService(CreateClient(
            ("GetPlayerSummaries", summaries),
            ("GetRecentlyPlayedGames", recent)));

        var result = await service.GetUserActivitiesAsync(new[] { "200" });

        var activity = Assert.Single(result);
        Assert.Equal("Bob", activity.PersonaName);
        Assert.Empty(activity.RecentGames);
        Assert.Null(activity.CurrentGameTitle);
    }
}
