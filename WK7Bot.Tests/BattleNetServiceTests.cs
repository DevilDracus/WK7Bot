using System.Net;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using WK7Bot.Options;
using WK7Bot.Services;
using Xunit;

namespace WK7Bot.Tests;

public class BattleNetServiceTests
{
    private const string TestRefreshToken = "refresh-token-123";
    private const string TestClientId = "client-id";
    private const string TestClientSecret = "client-secret";
    private const string TestDiscordUserId = "111";

    private const string TokenJson =
        """{"access_token":"access-token-1","expires_in":3600,"scope":"openid wow.profile d3.profile","token_type":"bearer"}""";

    private const string UserInfoJson =
        """{"sub":"1234567890","id":1234567890,"battletag":"Testuser#1234"}""";

    private const string UserInfoWithoutBattleTagJson =
        """{"sub":"1234567890","id":1234567890}""";

    private const string DiabloJson =
        """{"battleTag":"Testuser#1234","heroes":[{"name":"Arthas","class":"death-knight","level":70,"paragonLevel":1200,"seasonal":true,"hardcore":false,"dead":false}]}""";

    private const string MediaJsonWithAvatar =
        """{"assets":[{"key":"avatar","value":"https://render.example/avatar.jpg"},{"key":"bust","value":"https://render.example/bust.jpg"},{"key":"profile","value":"https://render.example/profile.jpg"}]}""";

    private const string MediaJsonWithoutAvatar =
        """{"assets":[{"key":"bust","value":"https://render.example/bust.jpg"},{"key":"profile","value":"https://render.example/profile.jpg"}]}""";

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
        private int _callCount;

        public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            _responder = responder;
        }

        public int CallCount => _callCount;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            // The service fetches characters with Task.WhenAll, so the counter must be thread-safe.
            Interlocked.Increment(ref _callCount);
            return Task.FromResult(_responder(request));
        }
    }

    private sealed class TestableBattleNetService : BattleNetService
    {
        public TestableBattleNetService(
            HttpClient httpClient,
            IMemoryCache cache,
            IOptions<Wk7BotOptions> options,
            ILogger<BattleNetService> logger)
            : base(httpClient, cache, options, logger)
        {
        }

        protected override TimeSpan RetryDelay => TimeSpan.Zero;
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        private readonly object _gate = new();
        private readonly List<(LogLevel Level, string Message)> _entries = new();

        public IReadOnlyList<(LogLevel Level, string Message)> Entries
        {
            get
            {
                lock (_gate)
                {
                    return _entries.ToList();
                }
            }
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            // Log calls arrive from concurrent character fetches; serialize the buffered writes.
            lock (_gate)
            {
                _entries.Add((logLevel, formatter(state, exception)));
            }
        }
    }

    /// <summary>
    /// Routes requests to per-endpoint responses while counting calls, mirroring the Steam test stubs.
    /// </summary>
    private sealed class FakeBattleNetApi
    {
        private int _tokenCalls;
        private int _userInfoCalls;
        private int _accountProfileCalls;
        private int _characterProfileCalls;
        private int _mediaCalls;
        private int _diabloCalls;

        public readonly ConcurrentQueue<string> Urls = new();

        public Func<HttpResponseMessage> TokenResponse = () => Json(TokenJson);
        public Func<HttpResponseMessage> UserInfoResponse = () => Json(UserInfoJson);
        public Func<HttpResponseMessage> AccountProfileResponse = () => Json(AccountProfileJson("testuser", "altchar"));
        public Func<string, HttpResponseMessage> CharacterResponse = name => Json(CharacterJson(name, name == "testuser" ? 80 : 70));
        public Func<HttpResponseMessage> MediaResponse = () => Json(MediaJsonWithAvatar);
        public Func<HttpResponseMessage> DiabloResponse = () => Json(DiabloJson);

        public int TokenCalls => _tokenCalls;
        public int UserInfoCalls => _userInfoCalls;
        public int AccountProfileCalls => _accountProfileCalls;
        public int CharacterProfileCalls => _characterProfileCalls;
        public int MediaCalls => _mediaCalls;
        public int DiabloCalls => _diabloCalls;

        public StubHttpMessageHandler CreateHandler() => new(Respond);

        private HttpResponseMessage Respond(HttpRequestMessage request)
        {
            string url = request.RequestUri!.OriginalString;
            Urls.Enqueue(url);

            if (url.Contains("/oauth/token"))
            {
                Interlocked.Increment(ref _tokenCalls);
                return TokenResponse();
            }

            if (url.Contains("/oauth/userinfo"))
            {
                Interlocked.Increment(ref _userInfoCalls);
                return UserInfoResponse();
            }

            if (url.Contains("/profile/user/wow"))
            {
                Interlocked.Increment(ref _accountProfileCalls);
                return AccountProfileResponse();
            }

            if (url.Contains("character-media"))
            {
                Interlocked.Increment(ref _mediaCalls);
                return MediaResponse();
            }

            if (url.Contains("/profile/wow/character/"))
            {
                Interlocked.Increment(ref _characterProfileCalls);
                return CharacterResponse(ExtractCharacterName(url));
            }

            if (url.Contains("/d3/profile/"))
            {
                Interlocked.Increment(ref _diabloCalls);
                return DiabloResponse();
            }

            return Json("{}");
        }

        private static string ExtractCharacterName(string url)
        {
            var path = url.Split('?')[0];
            return Uri.UnescapeDataString(path.Split('/')[^1]);
        }
    }

    private static BattleNetService CreateService(
        HttpMessageHandler handler,
        string? clientId = TestClientId,
        string? clientSecret = TestClientSecret,
        string? region = null,
        string? locale = null,
        List<DiscordBattleNetMappingOptions>? mappings = null,
        ILogger<BattleNetService>? logger = null)
    {
        var httpClient = new HttpClient(handler);
        var cache = new MemoryCache(new MemoryCacheOptions());
        var options = Microsoft.Extensions.Options.Options.Create(new Wk7BotOptions
        {
            BattleNetClientId = clientId,
            BattleNetClientSecret = clientSecret,
            BattleNetRegion = region ?? "eu",
            BattleNetLocale = locale ?? "de_DE",
            DiscordBattleNetMappings = mappings ?? new List<DiscordBattleNetMappingOptions>()
        });

        return new TestableBattleNetService(httpClient, cache, options, logger ?? NullLogger<BattleNetService>.Instance);
    }

    private static List<DiscordBattleNetMappingOptions> Mapping(
        string refreshToken = TestRefreshToken,
        string region = "",
        string battleTag = "")
    {
        return new List<DiscordBattleNetMappingOptions>
        {
            new()
            {
                DiscordUserId = TestDiscordUserId,
                RefreshToken = refreshToken,
                Region = region,
                BattleTag = battleTag
            }
        };
    }

    private static HttpResponseMessage Json(string json, HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        return new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
        };
    }

    private static string AccountProfileJson(params string[] characterNames)
    {
        var characters = string.Join(",", characterNames.Select(name =>
            "{\"key\":{\"href\":\"https://eu.api.blizzard.com/profile/wow/character/azuremyst/" + name
            + "?namespace=profile-eu&locale=de_DE\"}}"));

        return $$"""{"wow_accounts":[{"characters":[{{characters}}]}]}""";
    }

    private static string CharacterJson(string name, int level, long lastLoginTimestamp = 1730000000000)
    {
        return $$"""
        {
          "name": "{{name}}",
          "realm": {"name": "Azuremyst", "id": 12, "slug": "azuremyst"},
          "character_class": {"name": "Magier"},
          "faction": {"name": "Horde"},
          "guild": {"name": "WK7"},
          "level": {{level}},
          "average_item_level": 58.4,
          "equipped_item_level": 60.1,
          "last_login_timestamp": {{lastLoginTimestamp}}
        }
        """;
    }

    [Fact]
    public void GetMappingForDiscordUser_ReturnsMappedAccount()
    {
        var service = CreateService(
            new StubHttpMessageHandler(_ => Json("{}")),
            mappings: Mapping(region: "us", battleTag: "Testuser#1234"));

        var mapping = service.GetMappingForDiscordUser(TestDiscordUserId);

        Assert.NotNull(mapping);
        Assert.Equal(TestRefreshToken, mapping!.RefreshToken);
        Assert.Equal("us", mapping.Region);
        Assert.Equal("Testuser#1234", mapping.BattleTag);
    }

    [Fact]
    public void GetMappingForDiscordUser_IsCaseInsensitive()
    {
        var service = CreateService(
            new StubHttpMessageHandler(_ => Json("{}")),
            mappings: new List<DiscordBattleNetMappingOptions>
            {
                new() { DiscordUserId = "ABCdef123", RefreshToken = TestRefreshToken }
            });

        Assert.NotNull(service.GetMappingForDiscordUser("abcdef123"));
    }

    [Fact]
    public void GetMappingForDiscordUser_ReturnsNull_WhenNoMappingExists()
    {
        var service = CreateService(
            new StubHttpMessageHandler(_ => Json("{}")),
            mappings: new List<DiscordBattleNetMappingOptions>());

        Assert.Null(service.GetMappingForDiscordUser("999"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task GetBattleNetUserDataAsync_Throws_WhenRefreshTokenIsNullOrWhiteSpace(string refreshToken)
    {
        var service = CreateService(new StubHttpMessageHandler(_ => Json("{}")));

        await Assert.ThrowsAnyAsync<ArgumentException>(() => service.GetBattleNetUserDataAsync(refreshToken));
    }

    [Theory]
    [InlineData("", "secret")]
    [InlineData("id", "")]
    public async Task GetBattleNetUserDataAsync_ReturnsNull_WhenClientCredentialsMissing(string clientId, string clientSecret)
    {
        var handler = new StubHttpMessageHandler(_ => Json("{}"));
        var logger = new CapturingLogger<BattleNetService>();
        var service = CreateService(handler, clientId: clientId, clientSecret: clientSecret, logger: logger);

        var result = await service.GetBattleNetUserDataAsync(TestRefreshToken);

        Assert.Null(result);
        Assert.Equal(0, handler.CallCount);
        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task GetBattleNetUserDataAsync_ParsesIdentityCharactersAndDiabloHeroes()
    {
        var api = new FakeBattleNetApi();
        var service = CreateService(api.CreateHandler(), mappings: Mapping());

        var result = await service.GetBattleNetUserDataAsync(TestRefreshToken);

        Assert.NotNull(result);
        Assert.Equal("1234567890", result!.BattleNetId);
        Assert.Equal("Testuser#1234", result.BattleTag);
        Assert.Equal("eu", result.Region);

        Assert.Equal(2, result.WowCharacters.Count);
        var main = result.WowCharacters[0];
        Assert.Equal("testuser", main.Name);
        Assert.Equal(80, main.Level);
        Assert.Equal("Magier", main.CharacterClass);
        Assert.Equal("Horde", main.Faction);
        Assert.Equal("Azuremyst", main.Realm);
        Assert.Equal("azuremyst", main.RealmSlug);
        Assert.Equal("WK7", main.GuildName);
        Assert.Equal(58.4, main.AverageItemLevel);
        Assert.Equal(60.1, main.EquippedItemLevel);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1730000000000), main.LastLoginTimestamp);
        Assert.Equal("https://render.example/avatar.jpg", main.AvatarUrl);
        Assert.Equal(70, result.WowCharacters[1].Level);

        Assert.Equal("https://render.example/avatar.jpg", result.AvatarUrl);
        Assert.Equal("testuser · Stufe 80", result.MainCharacterDisplay);

        var hero = Assert.Single(result.DiabloHeroes);
        Assert.Equal("Arthas", hero.Name);
        Assert.Equal("death-knight", hero.HeroClass);
        Assert.Equal(70, hero.Level);
        Assert.Equal(1200, hero.ParagonLevel);
        Assert.True(hero.Seasonal);
        Assert.False(hero.Hardcore);
        Assert.False(hero.Dead);

        Assert.Equal(1, api.TokenCalls);
        Assert.Equal(1, api.UserInfoCalls);
        Assert.Equal(1, api.AccountProfileCalls);
        Assert.Equal(2, api.CharacterProfileCalls);
        Assert.Equal(2, api.MediaCalls);
        Assert.Equal(1, api.DiabloCalls);
    }

    [Fact]
    public async Task GetBattleNetUserDataAsync_UsesDefaultRegionAndLocale()
    {
        var api = new FakeBattleNetApi();
        var service = CreateService(api.CreateHandler(), mappings: Mapping());

        await service.GetBattleNetUserDataAsync(TestRefreshToken);

        Assert.Contains(api.Urls, u => u.StartsWith("https://eu.battle.net/oauth/token"));
        Assert.Contains(api.Urls, u => u.StartsWith("https://eu.battle.net/oauth/userinfo"));
        Assert.Contains(api.Urls, u => u.Contains("https://eu.api.blizzard.com/profile/user/wow?namespace=profile-eu&locale=de_DE"));
    }

    [Fact]
    public async Task GetBattleNetUserDataAsync_PrefersMappingRegion_AndUsesConfiguredLocale()
    {
        var api = new FakeBattleNetApi();
        var service = CreateService(api.CreateHandler(), region: "us", locale: "en_US", mappings: Mapping(region: "kr"));

        await service.GetBattleNetUserDataAsync(TestRefreshToken);

        Assert.Contains(api.Urls, u => u.StartsWith("https://kr.battle.net/oauth/token"));
        Assert.Contains(api.Urls, u => u.Contains("namespace=profile-kr&locale=en_US"));
        Assert.DoesNotContain(api.Urls, u => u.StartsWith("https://us.battle.net") || u.StartsWith("https://eu.battle.net"));
    }

    [Fact]
    public async Task GetBattleNetUserDataAsync_UsesMappingBattleTag_WhenUserinfoHasNoBattleTag()
    {
        var api = new FakeBattleNetApi { UserInfoResponse = () => Json(UserInfoWithoutBattleTagJson) };
        var service = CreateService(api.CreateHandler(), mappings: Mapping(battleTag: "Fallback#42"));

        var result = await service.GetBattleNetUserDataAsync(TestRefreshToken);

        Assert.NotNull(result);
        Assert.Equal("Fallback#42", result!.BattleTag);
        Assert.Equal("1234567890", result.BattleNetId);
        Assert.NotEmpty(result.WowCharacters);
    }

    [Fact]
    public async Task GetBattleNetUserDataAsync_ReturnsNull_WhenNoBattleTagAvailable()
    {
        var api = new FakeBattleNetApi { UserInfoResponse = () => Json(UserInfoWithoutBattleTagJson) };
        var service = CreateService(api.CreateHandler(), mappings: Mapping());

        var result = await service.GetBattleNetUserDataAsync(TestRefreshToken);

        Assert.Null(result);
        Assert.Equal(0, api.AccountProfileCalls);
    }

    [Fact]
    public async Task GetBattleNetUserDataAsync_FallsBackToBustArt_WhenAvatarAssetMissing()
    {
        var api = new FakeBattleNetApi { MediaResponse = () => Json(MediaJsonWithoutAvatar) };
        var service = CreateService(api.CreateHandler(), mappings: Mapping());

        var result = await service.GetBattleNetUserDataAsync(TestRefreshToken);

        Assert.NotNull(result);
        Assert.Equal("https://render.example/bust.jpg", result!.WowCharacters[0].AvatarUrl);
        Assert.Equal("https://render.example/bust.jpg", result.AvatarUrl);
    }

    [Fact]
    public async Task GetBattleNetUserDataAsync_SortsCharactersByLevelDescending()
    {
        var api = new FakeBattleNetApi
        {
            AccountProfileResponse = () => Json(AccountProfileJson("altchar", "testuser"))
        };
        var service = CreateService(api.CreateHandler(), mappings: Mapping());

        var result = await service.GetBattleNetUserDataAsync(TestRefreshToken);

        Assert.NotNull(result);
        Assert.Equal(new[] { "testuser", "altchar" }, result!.WowCharacters.Select(c => c.Name).ToArray());
    }

    [Fact]
    public async Task GetBattleNetUserDataAsync_CapsCharactersAtFive()
    {
        var api = new FakeBattleNetApi
        {
            AccountProfileResponse = () => Json(AccountProfileJson("c1", "c2", "c3", "c4", "c5", "c6", "c7")),
            CharacterResponse = name => Json(CharacterJson(name, 70))
        };
        var service = CreateService(api.CreateHandler(), mappings: Mapping());

        var result = await service.GetBattleNetUserDataAsync(TestRefreshToken);

        Assert.NotNull(result);
        Assert.Equal(5, result!.WowCharacters.Count);
        Assert.Equal(5, api.CharacterProfileCalls);
        Assert.Equal(5, api.MediaCalls);
    }

    [Fact]
    public async Task GetBattleNetUserDataAsync_SkipsCharacter_WhenProfileFails()
    {
        var api = new FakeBattleNetApi
        {
            CharacterResponse = name => name == "altchar"
                ? new HttpResponseMessage(HttpStatusCode.NotFound)
                : Json(CharacterJson(name, 80))
        };
        var service = CreateService(api.CreateHandler(), mappings: Mapping());

        var result = await service.GetBattleNetUserDataAsync(TestRefreshToken);

        Assert.NotNull(result);
        var character = Assert.Single(result!.WowCharacters);
        Assert.Equal("testuser", character.Name);
    }

    [Fact]
    public async Task GetBattleNetUserDataAsync_ContinuesWithoutWoW_WhenAccountProfileFails()
    {
        var api = new FakeBattleNetApi { AccountProfileResponse = () => new HttpResponseMessage(HttpStatusCode.Unauthorized) };
        var service = CreateService(api.CreateHandler(), mappings: Mapping());

        var result = await service.GetBattleNetUserDataAsync(TestRefreshToken);

        Assert.NotNull(result);
        Assert.Equal("Testuser#1234", result!.BattleTag);
        Assert.Empty(result.WowCharacters);
        Assert.Null(result.AvatarUrl);
        Assert.Null(result.MainCharacterDisplay);
        Assert.Equal(0, api.CharacterProfileCalls);
        Assert.Equal(1, api.DiabloCalls);
    }

    [Fact]
    public async Task GetBattleNetUserDataAsync_ContinuesWithoutDiablo_WhenDiabloProfileFails()
    {
        var api = new FakeBattleNetApi { DiabloResponse = () => new HttpResponseMessage(HttpStatusCode.NotFound) };
        var service = CreateService(api.CreateHandler(), mappings: Mapping());

        var result = await service.GetBattleNetUserDataAsync(TestRefreshToken);

        Assert.NotNull(result);
        Assert.Equal(2, result!.WowCharacters.Count);
        Assert.Empty(result.DiabloHeroes);
    }

    [Fact]
    public async Task GetBattleNetUserDataAsync_RetriesTransientFailure_ThenSucceeds()
    {
        var api = new FakeBattleNetApi();
        api.UserInfoResponse = () => api.UserInfoCalls == 1
            ? new HttpResponseMessage(HttpStatusCode.TooManyRequests)
            : Json(UserInfoJson);
        var service = CreateService(api.CreateHandler(), mappings: Mapping());

        var result = await service.GetBattleNetUserDataAsync(TestRefreshToken);

        Assert.NotNull(result);
        Assert.Equal(2, api.UserInfoCalls);
    }

    [Fact]
    public async Task GetBattleNetUserDataAsync_GivesUp_AfterMaxAttempts()
    {
        var api = new FakeBattleNetApi { UserInfoResponse = () => new HttpResponseMessage(HttpStatusCode.InternalServerError) };
        var service = CreateService(api.CreateHandler(), mappings: Mapping());

        var result = await service.GetBattleNetUserDataAsync(TestRefreshToken);

        Assert.Null(result);
        Assert.Equal(3, api.UserInfoCalls);
        Assert.Equal(0, api.AccountProfileCalls);
    }

    [Fact]
    public async Task GetBattleNetUserDataAsync_ReturnsNull_OnHttpRequestFailure()
    {
        var handler = new StubHttpMessageHandler(_ => throw new HttpRequestException("connection reset"));
        var service = CreateService(handler, mappings: Mapping());

        var result = await service.GetBattleNetUserDataAsync(TestRefreshToken);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetBattleNetUserDataAsync_ReusesCachedAccessToken()
    {
        var api = new FakeBattleNetApi();
        var service = CreateService(api.CreateHandler(), mappings: Mapping());

        var first = await service.GetBattleNetUserDataAsync(TestRefreshToken);
        var second = await service.GetBattleNetUserDataAsync(TestRefreshToken);

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal(1, api.TokenCalls);
        Assert.Equal(2, api.UserInfoCalls);
    }

    [Fact]
    public async Task GetBattleNetUserDataAsync_RefreshesToken_WhenCachedTokenRejected()
    {
        var api = new FakeBattleNetApi();
        api.UserInfoResponse = () => api.UserInfoCalls == 2
            ? new HttpResponseMessage(HttpStatusCode.Unauthorized)
            : Json(UserInfoJson);
        var service = CreateService(api.CreateHandler(), mappings: Mapping());

        var first = await service.GetBattleNetUserDataAsync(TestRefreshToken);
        var second = await service.GetBattleNetUserDataAsync(TestRefreshToken);

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal(2, api.TokenCalls);
        Assert.Equal(3, api.UserInfoCalls);
    }

    [Fact]
    public async Task GetBattleNetUserDataAsync_ReturnsNull_WhenTokenEndpointRejectsRefresh()
    {
        var api = new FakeBattleNetApi { TokenResponse = () => new HttpResponseMessage(HttpStatusCode.BadRequest) };
        var service = CreateService(api.CreateHandler(), mappings: Mapping());

        var result = await service.GetBattleNetUserDataAsync(TestRefreshToken);

        Assert.Null(result);
        Assert.Equal(1, api.TokenCalls);
        Assert.Equal(0, api.UserInfoCalls);
    }

    [Fact]
    public async Task GetBattleNetUserDataAsync_ParsesSecondPrecisionTimestamps()
    {
        var api = new FakeBattleNetApi
        {
            AccountProfileResponse = () => Json(AccountProfileJson("testuser")),
            CharacterResponse = _ => Json(CharacterJson("testuser", 60, lastLoginTimestamp: 1730000000))
        };
        var service = CreateService(api.CreateHandler(), mappings: Mapping());

        var result = await service.GetBattleNetUserDataAsync(TestRefreshToken);

        Assert.NotNull(result);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1730000000), result!.WowCharacters[0].LastLoginTimestamp);
    }

    [Fact]
    public async Task GetBattleNetUserDataAsync_EscapesBattleTagInDiabloProfileUrl()
    {
        var api = new FakeBattleNetApi();
        var service = CreateService(api.CreateHandler(), mappings: Mapping());

        await service.GetBattleNetUserDataAsync(TestRefreshToken);

        Assert.Contains(api.Urls, u => u.Contains("/d3/profile/Testuser%231234/"));
    }
}
