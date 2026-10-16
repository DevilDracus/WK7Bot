using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;
using WK7Bot.Core.Entities;
using WK7Bot.Core.Interfaces;
using WK7Bot.Options;
using WK7Bot.Services;
using Xunit;

namespace WK7Bot.Tests;

public class AccountLinkServiceTests
{
    private static HttpClient CreateHttpClient(string json = "{\"response\":{\"success\":1,\"steamid\":\"76561198000000000\"}}")
        => new(new StubHandler(json));

    private sealed class StubHandler(string json) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) });
    }

    private static AccountLinkService CreateService(IUserAccountLinkRepository repository, HttpClient client, bool autoWrite = false)
    {
        var writer = new AddOnOptionsWriter(new HttpClient(), NullLogger<AddOnOptionsWriter>.Instance);
        return new AccountLinkService(
            repository,
            writer,
            Microsoft.Extensions.Options.Options.Create(new Wk7BotOptions { LinkAutoWriteOptions = autoWrite, SteamApiKey = "test-key" }),
            client,
            NullLogger<AccountLinkService>.Instance);
    }

    [Fact]
    public async Task LinkSteamAsync_StoresLink_ForBareSteamId()
    {
        var repository = new Mock<IUserAccountLinkRepository>();
        UserAccountLink? captured = null;
        repository.Setup(r => r.UpsertAsync(It.IsAny<UserAccountLink>(), It.IsAny<CancellationToken>()))
            .Callback<UserAccountLink, CancellationToken>((link, _) => captured = link)
            .Returns(Task.CompletedTask);

        var result = await CreateService(repository.Object, CreateHttpClient()).LinkSteamAsync(111, "76561198000000000");

        Assert.True(result.Success);
        Assert.NotNull(captured);
        Assert.Equal("steam", captured!.Provider);
        Assert.Equal("76561198000000000", captured.ExternalId);
    }

    [Fact]
    public async Task LinkSteamAsync_ResolvesVanityUrl()
    {
        var repository = new Mock<IUserAccountLinkRepository>();
        repository.Setup(r => r.UpsertAsync(It.IsAny<UserAccountLink>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var result = await CreateService(repository.Object, CreateHttpClient()).LinkSteamAsync(111, "https://steamcommunity.com/id/dracus/");

        Assert.True(result.Success);
        Assert.Equal("76561198000000000", result.ExternalId);
    }

    [Fact]
    public async Task LinkSteamAsync_Fails_ForUnparseableInput()
    {
        var repository = new Mock<IUserAccountLinkRepository>();
        var result = await CreateService(repository.Object, CreateHttpClient()).LinkSteamAsync(111, "not-a-steam-id");

        Assert.False(result.Success);
        repository.Verify(r => r.UpsertAsync(It.IsAny<UserAccountLink>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task LinkWishlistAsync_StoresWishlistUrl()
    {
        var repository = new Mock<IUserAccountLinkRepository>();
        UserAccountLink? captured = null;
        repository.Setup(r => r.UpsertAsync(It.IsAny<UserAccountLink>(), It.IsAny<CancellationToken>()))
            .Callback<UserAccountLink, CancellationToken>((link, _) => captured = link)
            .Returns(Task.CompletedTask);

        var result = await CreateService(repository.Object, CreateHttpClient())
            .LinkWishlistAsync(111, "https://store.steampowered.com/wishlist/profiles/76561198000000000/");

        Assert.True(result.Success);
        Assert.Equal("76561198000000000", captured!.ExternalId);
        Assert.Contains("wishlist", captured.WishlistUrl);
    }

    [Fact]
    public async Task UnlinkAsync_ReturnsFalse_WhenMissing()
    {
        var repository = new Mock<IUserAccountLinkRepository>();
        repository.Setup(r => r.RemoveAsync(111, "steam", It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var result = await CreateService(repository.Object, CreateHttpClient()).UnlinkAsync(111, "steam");

        Assert.False(result.Success);
    }
}
