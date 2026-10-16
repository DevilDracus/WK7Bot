using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WK7Bot.Options;
using WK7Bot.Services;
using Xunit;

namespace WK7Bot.Tests;

public class ExternalApiTransientClassificationTests
{
    private sealed class ThrowingHttpMessageHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            // The handler is not honoring an explicit cancellation token. This represents the
            // external timeout that the HttpClient timeout itself produces after ~30 seconds,
            // which the services must classify as a transient warning rather than an unfriendly error.
            throw new TaskCanceledException("simulated external API timeout");
        }
    }

    private static SteamService CreateSteamService()
    {
        var handler = new ThrowingHttpMessageHandler();
        var httpClient = new HttpClient(handler);
        var cache = new MemoryCache(new MemoryCacheOptions());
        var options = Microsoft.Extensions.Options.Options.Create(new Wk7BotOptions
        {
            SteamApiKey = "test-api-key"
        });

        return new SteamService(httpClient, cache, options, NullLogger<SteamService>.Instance);
    }

    private static BattleNetService CreateBattleNetService()
    {
        var handler = new ThrowingHttpMessageHandler();
        var httpClient = new HttpClient(handler);
        var cache = new MemoryCache(new MemoryCacheOptions());
        var options = Microsoft.Extensions.Options.Options.Create(new Wk7BotOptions
        {
            BattleNetClientId = "client-id",
            BattleNetClientSecret = "client-secret",
            BattleNetRegion = "eu"
        });

        return new BattleNetService(httpClient, cache, options, NullLogger<BattleNetService>.Instance);
    }

    [Fact]
    public async Task Steam_GetEnrichedAchievements_FromTimeout_ReturnsEmpty()
    {
        var service = CreateSteamService();

        var result = await service.GetEnrichedAchievementsAsync(
            steamId: "76561198012345678",
            appId: 1104330u,
            cancellationToken: CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task BattleNet_GetBattleNetUserData_FromTimeout_ReturnsNull()
    {
        var service = CreateBattleNetService();

        var result = await service.GetBattleNetUserDataAsync(
            refreshToken: "test-refresh-token",
            cancellationToken: CancellationToken.None);

        Assert.Null(result);
    }
}
