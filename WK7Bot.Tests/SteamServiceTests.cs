using System.Net;
using System.Text;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WK7Bot.Models;
using WK7Bot.Options;
using WK7Bot.Services;
using Xunit;

namespace WK7Bot.Tests;

public class SteamServiceTests
{
    private const string TestSteamId = "76561198012345678";
    private const string TestApiKey = "test_api_key";

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            _responder = responder;
        }

        public int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(_responder(request));
        }
    }

    private sealed class TestableSteamService : SteamService
    {
        public TestableSteamService(
            HttpClient httpClient,
            IMemoryCache cache,
            IOptions<Wk7BotOptions> options,
            ILogger<SteamService> logger)
            : base(httpClient, cache, options, logger)
        {
        }

        protected override TimeSpan RetryDelay => TimeSpan.Zero;
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, formatter(state, exception)));
    }

    private static SteamService CreateService(
        HttpMessageHandler handler,
        string? apiKey = TestApiKey,
        List<DiscordSteamMappingOptions>? mappings = null,
        ILogger<SteamService>? logger = null)
    {
        var httpClient = new HttpClient(handler);
        var cache = new MemoryCache(new MemoryCacheOptions());
        var options = Microsoft.Extensions.Options.Options.Create(new Wk7BotOptions
        {
            SteamApiKey = apiKey,
            DiscordSteamMappings = mappings ?? new List<DiscordSteamMappingOptions>()
        });

        return new TestableSteamService(httpClient, cache, options, logger ?? NullLogger<SteamService>.Instance);
    }

    private static HttpResponseMessage Json(string json, HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        return new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    [Fact]
    public void GetSteamIdForDiscordUser_ReturnsMappedSteamId()
    {
        var service = CreateService(
            new StubHttpMessageHandler(_ => Json("{}")),
            mappings: new List<DiscordSteamMappingOptions>
            {
                new() { DiscordUserId = "111", SteamId = TestSteamId }
            });

        Assert.Equal(TestSteamId, service.GetSteamIdForDiscordUser("111"));
    }

    [Fact]
    public void GetSteamIdForDiscordUser_IsCaseInsensitive()
    {
        var service = CreateService(
            new StubHttpMessageHandler(_ => Json("{}")),
            mappings: new List<DiscordSteamMappingOptions>
            {
                new() { DiscordUserId = "ABCdef123", SteamId = TestSteamId }
            });

        Assert.Equal(TestSteamId, service.GetSteamIdForDiscordUser("abcdef123"));
    }

    [Fact]
    public void GetSteamIdForDiscordUser_ReturnsNull_WhenNoMappingExists()
    {
        var service = CreateService(
            new StubHttpMessageHandler(_ => Json("{}")),
            mappings: new List<DiscordSteamMappingOptions>());

        Assert.Null(service.GetSteamIdForDiscordUser("999"));
    }

    [Fact]
    public async Task GetSteamUserDataAsync_ReturnsNull_WhenApiKeyMissing()
    {
        var handler = new StubHttpMessageHandler(_ => Json("{}"));
        var service = CreateService(handler, apiKey: "");

        var result = await service.GetSteamUserDataAsync(TestSteamId);

        Assert.Null(result);
        Assert.Equal(0, handler.CallCount);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task GetSteamUserDataAsync_Throws_WhenSteamIdIsNullOrWhiteSpace(string steamId)
    {
        var service = CreateService(new StubHttpMessageHandler(_ => Json("{}")));

        await Assert.ThrowsAnyAsync<ArgumentException>(() => service.GetSteamUserDataAsync(steamId));
    }

    [Fact]
    public async Task GetSteamUserDataAsync_ReturnsNull_WhenPlayerNotFound()
    {
        var handler = new StubHttpMessageHandler(_ => Json("""{"response":{"players":[]}}"""));
        var service = CreateService(handler);

        var result = await service.GetSteamUserDataAsync(TestSteamId);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetSteamUserDataAsync_ParsesPlayerSummary()
    {
        var handler = new StubHttpMessageHandler(request =>
        {
            var url = request.RequestUri!.ToString();
            if (url.Contains("GetPlayerSummaries"))
            {
                return Json("""
                {
                  "response": {
                    "players": [
                      {
                        "steamid": "76561198012345678",
                        "personaname": "TestUser",
                        "personastate": 1,
                        "gameextrainfo": "Counter-Strike 2",
                        "gameid": "730"
                      }
                    ]
                  }
                }
                """);
            }

            if (url.Contains("GetRecentlyPlayedGames"))
            {
                return Json("""
                {
                  "response": {
                    "games": [
                      { "appid": 730, "name": "Counter-Strike 2", "playtime_2weeks": 600, "playtime_forever": 12000 }
                    ]
                  }
                }
                """);
            }

            return Json("{}");
        });

        var service = CreateService(handler);

        var result = await service.GetSteamUserDataAsync(TestSteamId);

        Assert.NotNull(result);
        Assert.Equal(TestSteamId, result!.SteamId);
        Assert.Equal("TestUser", result.PersonaName);
        Assert.Equal("Online", result.PersonaState);
        Assert.Equal("Counter-Strike 2", result.CurrentGameTitle);
        Assert.Equal(730u, result.CurrentGameAppId);
        Assert.Single(result.RecentGames);
        Assert.Equal(600, result.PlaytimeLastTwoWeeksMinutes);
        Assert.Equal(12000, result.RecentGames[0].PlaytimeForeverMinutes);
    }

    [Fact]
    public async Task GetSteamUserDataAsync_ReturnsUserData_WhenNoCurrentGame()
    {
        var handler = new StubHttpMessageHandler(request =>
        {
            var url = request.RequestUri!.ToString();
            if (url.Contains("GetPlayerSummaries"))
            {
                return Json("""
                {
                  "response": {
                    "players": [
                      { "steamid": "76561198012345678", "personaname": "IdleUser", "personastate": 0 }
                    ]
                  }
                }
                """);
            }

            return Json("""{"response":{}}""");
        });

        var service = CreateService(handler);

        var result = await service.GetSteamUserDataAsync(TestSteamId);

        Assert.NotNull(result);
        Assert.Equal("Offline", result!.PersonaState);
        Assert.Null(result.CurrentGameTitle);
        Assert.Null(result.CurrentGameAppId);
        Assert.Empty(result.RecentGames);
        Assert.Empty(result.RecentAchievements);
    }

    [Fact]
    public async Task GetSteamUserDataAsync_ReturnsNull_OnHttpRequestFailure()
    {
        var handler = new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.InternalServerError));

        var service = CreateService(handler);

        var result = await service.GetSteamUserDataAsync(TestSteamId);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetEnrichedAchievementsAsync_ReturnsEmpty_WhenApiFails()
    {
        var handler = new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.BadRequest));

        var service = CreateService(handler);

        var result = await service.GetEnrichedAchievementsAsync(TestSteamId, 730);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetEnrichedAchievementsAsync_MergesSchemaIcons()
    {
        var handler = new StubHttpMessageHandler(request =>
        {
            var url = request.RequestUri!.ToString();
            if (url.Contains("GetSchemaForGame"))
            {
                return Json("""
                {
                  "game": {
                    "availableGameStats": {
                      "achievements": [
                        { "name": "achievement_one", "icon": "https://example.com/icon1.png" },
                        { "name": "achievement_two", "icon": "https://example.com/icon2.png" }
                      ]
                    }
                  }
                }
                """);
            }

            if (url.Contains("GetPlayerAchievements"))
            {
                return Json("""
                            {
                              "playerstats": {
                                "success": true,
                                "Success": true,
                                "achievements": [
                                  { "apiname": "achievement_one", "achieved": 1, "unlocktime": 1700000000, "name": "First!", "description": "Did the thing" },
                                  { "apiname": "achievement_two", "achieved": 0, "unlocktime": 0 },
                                  { "apiname": "unknown_achievement", "achieved": 1, "unlocktime": 1600000000, "name": "Mystery", "description": "No schema" }
                                ]
                              }
                            }
                            """);
            }

            return Json("{}");
        });

        var service = CreateService(handler);

        var result = await service.GetEnrichedAchievementsAsync(TestSteamId, 730);

        Assert.Equal(2, result.Count);

        var withIcon = result.Single(a => a.ApiName == "achievement_one");
        Assert.Equal("https://example.com/icon1.png", withIcon.IconUrl);
        Assert.Equal("First!", withIcon.Name);
        Assert.NotNull(withIcon.UnlockTime);

        var noSchema = result.Single(a => a.ApiName == "unknown_achievement");
        Assert.Equal(string.Empty, noSchema.IconUrl);
        Assert.DoesNotContain(result, a => a.ApiName == "achievement_two");
    }

    [Fact]
    public async Task GetSteamUserDataAsync_CapsRecentAchievementsAtFive()
    {
        var achievements = string.Join(",", Enumerable.Range(1, 10).Select(i =>
            $$"""{"apiname":"ach_{{i}}","achieved":1,"unlocktime":{{1700000000 + i}},"name":"A{{i}}","description":"d"}"""));

        var handler = new StubHttpMessageHandler(request =>
        {
            var url = request.RequestUri!.ToString();
            if (url.Contains("GetPlayerSummaries"))
            {
                return Json("""
                {
                  "response": {
                    "players": [
                      { "steamid": "76561198012345678", "personaname": "AchUser", "personastate": 1, "gameextrainfo": "Game", "gameid": "440" }
                    ]
                  }
                }
                """);
            }

            if (url.Contains("GetRecentlyPlayedGames"))
            {
                return Json("""{"response":{"games":[]}}""");
            }

            if (url.Contains("GetPlayerAchievements"))
            {
                return Json($"{{\"playerstats\":{{\"success\":true,\"Success\":true,\"achievements\":[{achievements}]}}}}");
            }

            if (url.Contains("GetSchemaForGame"))
            {
                return Json("""{"game":{"availableGameStats":{"achievements":[]}}}""");
            }

            return Json("{}");
        });

        var service = CreateService(handler);

        var result = await service.GetSteamUserDataAsync(TestSteamId);

        Assert.NotNull(result);
        Assert.Equal(5, result!.RecentAchievements.Count);
        Assert.Equal("ach_10", result.RecentAchievements[0].ApiName);
        Assert.Equal("ach_6", result.RecentAchievements[4].ApiName);
    }

    [Fact]
    public async Task GetEnrichedAchievementsAsync_RetriesTransientFailure_ThenSucceeds()
    {
        int playerCalls = 0;

        var handler = new StubHttpMessageHandler(request =>
        {
            var url = request.RequestUri!.ToString();

            if (url.Contains("GetPlayerAchievements"))
            {
                playerCalls++;
                if (playerCalls == 1)
                {
                    return new HttpResponseMessage(HttpStatusCode.TooManyRequests);
                }

                return Json("""
                {
                  "playerstats": {
                    "success": true,
                    "achievements": [
                      { "apiname": "a1", "achieved": 1, "unlocktime": 1700000000, "name": "One", "description": "d" }
                    ]
                  }
                }
                """);
            }

            if (url.Contains("GetSchemaForGame"))
            {
                return Json("""
                {
                  "game": {
                    "availableGameStats": {
                      "achievements": [
                        { "name": "a1", "displayName": "One", "description": "d", "icon": "https://example.com/icon.png" }
                      ]
                    }
                  }
                }
                """);
            }

            return Json("{}");
        });

        var service = CreateService(handler);

        var result = await service.GetEnrichedAchievementsAsync(TestSteamId, 730);

        var achievement = Assert.Single(result);
        Assert.Equal("https://example.com/icon.png", achievement.IconUrl);
        Assert.Equal(2, playerCalls);
    }

    [Fact]
    public async Task GetEnrichedAchievementsAsync_DoesNotRetry_OnBadRequest()
    {
        int playerCalls = 0;

        var handler = new StubHttpMessageHandler(request =>
        {
            var url = request.RequestUri!.ToString();

            if (url.Contains("GetPlayerAchievements"))
            {
                playerCalls++;
                return Json("{}", HttpStatusCode.BadRequest);
            }

            return Json("{}");
        });

        var service = CreateService(handler);

        var result = await service.GetEnrichedAchievementsAsync(TestSteamId, 730);

        Assert.Empty(result);
        Assert.Equal(1, playerCalls);
    }

    [Fact]
    public async Task GetEnrichedAchievementsAsync_GivesUpAfterMaxAttempts_OnPersistentTransientFailure()
    {
        int playerCalls = 0;

        var handler = new StubHttpMessageHandler(request =>
        {
            var url = request.RequestUri!.ToString();

            if (url.Contains("GetPlayerAchievements"))
            {
                playerCalls++;
                return new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            }

            return Json("{}");
        });

        var service = CreateService(handler);

        var result = await service.GetEnrichedAchievementsAsync(TestSteamId, 730);

        Assert.Empty(result);
        Assert.Equal(3, playerCalls);
    }

    [Fact]
    public async Task GetEnrichedAchievementsAsync_DoesNotCacheFailedSchema()
    {
        int schemaCalls = 0;
        bool schemaHealthy = false;

        var handler = new StubHttpMessageHandler(request =>
        {
            var url = request.RequestUri!.ToString();

            if (url.Contains("GetSchemaForGame"))
            {
                schemaCalls++;
                if (!schemaHealthy)
                {
                    return new HttpResponseMessage(HttpStatusCode.InternalServerError);
                }

                return Json("""
                {
                  "game": {
                    "availableGameStats": {
                      "achievements": [
                        { "name": "a1", "displayName": "One", "description": "d", "icon": "https://example.com/icon.png" }
                      ]
                    }
                  }
                }
                """);
            }

            if (url.Contains("GetPlayerAchievements"))
            {
                return Json("""
                {
                  "playerstats": {
                    "success": true,
                    "achievements": [
                      { "apiname": "a1", "achieved": 1, "unlocktime": 1700000000, "name": "One", "description": "d" }
                    ]
                  }
                }
                """);
            }

            return Json("{}");
        });

        var service = CreateService(handler);

        var first = await service.GetEnrichedAchievementsAsync(TestSteamId, 730);
        var firstAchievement = Assert.Single(first);
        Assert.Equal(string.Empty, firstAchievement.IconUrl);
        Assert.Equal(3, schemaCalls);

        schemaHealthy = true;
        var second = await service.GetEnrichedAchievementsAsync(TestSteamId, 730);
        var secondAchievement = Assert.Single(second);
        Assert.Equal("https://example.com/icon.png", secondAchievement.IconUrl);
        Assert.Equal(4, schemaCalls);

        await service.GetEnrichedAchievementsAsync(TestSteamId, 730);
        Assert.Equal(4, schemaCalls);
    }

    [Fact]
    public async Task GetEnrichedAchievementsAsync_LogsWarning_WhenPlayerStatsUnsuccessful()
    {
        var logger = new CapturingLogger<SteamService>();

        var handler = new StubHttpMessageHandler(request =>
        {
            var url = request.RequestUri!.ToString();

            if (url.Contains("GetPlayerAchievements"))
            {
                return Json("""{"playerstats":{"success":false,"error":"There is no stats"}}""");
            }

            return Json("{}");
        });

        var service = CreateService(handler, logger: logger);

        var result = await service.GetEnrichedAchievementsAsync(TestSteamId, 730);

        Assert.Empty(result);
        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("There is no stats"));
    }

    [Fact]
    public async Task GetSteamUserDataAsync_LogsInformation_WhenNoGamesToFetch()
    {
        var logger = new CapturingLogger<SteamService>();

        var handler = new StubHttpMessageHandler(request =>
        {
            var url = request.RequestUri!.ToString();

            if (url.Contains("GetPlayerSummaries"))
            {
                return Json("""
                {
                  "response": {
                    "players": [
                      { "steamid": "76561198012345678", "personaname": "IdleUser", "personastate": 0 }
                    ]
                  }
                }
                """);
            }

            return Json("""{"response":{}}""");
        });

        var service = CreateService(handler, logger: logger);

        var result = await service.GetSteamUserDataAsync(TestSteamId);

        Assert.NotNull(result);
        Assert.Empty(result!.RecentAchievements);
        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Information && e.Message.Contains("skipping achievement fetch"));
    }

    [Fact]
    public async Task GetSteamUserDataAsync_LogsWarning_WhenRecentlyPlayedGamesFails()
    {
        var logger = new CapturingLogger<SteamService>();

        var handler = new StubHttpMessageHandler(request =>
        {
            var url = request.RequestUri!.ToString();

            if (url.Contains("GetPlayerSummaries"))
            {
                return Json("""
                {
                  "response": {
                    "players": [
                      { "steamid": "76561198012345678", "personaname": "TestUser", "personastate": 1 }
                    ]
                  }
                }
                """);
            }

            if (url.Contains("GetRecentlyPlayedGames"))
            {
                return new HttpResponseMessage(HttpStatusCode.InternalServerError);
            }

            return Json("{}");
        });

        var service = CreateService(handler, logger: logger);

        var result = await service.GetSteamUserDataAsync(TestSteamId);

        Assert.NotNull(result);
        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("GetRecentlyPlayedGames"));
    }
}
