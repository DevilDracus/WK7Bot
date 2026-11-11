using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Moq.Protected;
using WK7Bot.Services;
using Xunit;

namespace WK7Bot.Tests;

public class SteamStoreServiceTests
{
    private static HttpClient CreateClient(HttpResponseMessage response)
    {
        var handler = new Mock<HttpMessageHandler>();
        handler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(response);

        return new HttpClient(handler.Object)
        {
            BaseAddress = new Uri("https://store.steampowered.com/")
        };
    }

    private static SteamStoreService CreateService(HttpClient client)
        => new(client, NullLogger<SteamStoreService>.Instance);

    [Fact]
    public async Task SearchGameAsync_ReturnsBestMatch()
    {
        var json = @"{""items"":[{""id"":123,""name"":""Test Game""}]}";
        var service = CreateService(CreateClient(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json)
        }));

        var result = await service.SearchGameAsync("Test");

        Assert.NotNull(result);
        Assert.Equal(123u, result!.Value.AppId);
        Assert.Equal("Test Game", result.Value.Title);
    }

    [Fact]
    public async Task SearchGameAsync_ReturnsNull_WhenNoItems()
    {
        var json = @"{""items"":[]}";
        var service = CreateService(CreateClient(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json)
        }));

        var result = await service.SearchGameAsync("Unknown");

        Assert.Null(result);
    }

    [Fact]
    public async Task GetAppDetailsAsync_ReturnsReleaseDate()
    {
        var json = @"{""123"":{""success"":true,""data"":{""name"":""Test Game"",""release_date"":{""coming_soon"":false,""date"":""15 Jan, 2027""}}}}";
        var service = CreateService(CreateClient(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json)
        }));

        var result = await service.GetAppDetailsAsync(123);

        Assert.NotNull(result);
        Assert.Equal("Test Game", result!.Name);
        Assert.False(result.IsTba);
        Assert.False(result.IsComingSoon);
        Assert.Equal(new DateTime(2027, 1, 15), result.ReleaseDate);
    }

    [Fact]
    public async Task GetAppDetailsAsync_MarksTba_WhenDateMissing()
    {
        var json = @"{""123"":{""success"":true,""data"":{""name"":""Test Game"",""release_date"":{""coming_soon"":true,""date"":""""}}}}";
        var service = CreateService(CreateClient(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json)
        }));

        var result = await service.GetAppDetailsAsync(123);

        Assert.NotNull(result);
        Assert.True(result!.IsTba);
        Assert.True(result.IsComingSoon);
        Assert.Null(result.ReleaseDate);
    }

    [Fact]
    public async Task GetAppDetailsAsync_MarksTba_WhenDateContainsTba()
    {
        var json = @"{""123"":{""success"":true,""data"":{""name"":""Test Game"",""release_date"":{""coming_soon"":true,""date"":""TBA""}}}}";
        var service = CreateService(CreateClient(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json)
        }));

        var result = await service.GetAppDetailsAsync(123);

        Assert.NotNull(result);
        Assert.True(result!.IsTba);
        Assert.Null(result.ReleaseDate);
    }

    [Fact]
    public async Task GetAppDetailsAsync_ReturnsNull_WhenAppNotFound()
    {
        var json = @"{""123"":{""success"":false}}";
        var service = CreateService(CreateClient(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json)
        }));

        var result = await service.GetAppDetailsAsync(123);

        Assert.Null(result);
    }
}
