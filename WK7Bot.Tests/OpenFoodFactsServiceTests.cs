namespace WK7Bot.Tests;

using System.Net;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Moq.Protected;
using WK7Bot.Models;
using WK7Bot.Services;
using Xunit;

public class OpenFoodFactsServiceTests
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
            BaseAddress = new Uri("https://world.openfoodfacts.org/")
        };
    }

    private static OpenFoodFactsService CreateService(HttpClient client, IMemoryCache? cache = null)
        => new(client, NullLogger<OpenFoodFactsService>.Instance, cache ?? new MemoryCache(new MemoryCacheOptions()));

    [Fact]
    public async Task SearchProductAsync_ReturnsFirstProductWithName_WhenResultsExist()
    {
        var json = @"{""count"":2,""page"":1,""products"":[{""product_name"":""Banane"",""brands"":""Bio"",""quantity"":""1 kg"",""nutriscore_grade"":""a"",""url"":""https://world.openfoodfacts.org/product/123"",""nutriments"":{""energy-kcal_100g"":89,""proteins_100g"":1.1,""potassium_100g"":0.358,""phosphorus_100g"":0.022,""sodium_100g"":0.001}}]}";
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json)
        };

        var service = CreateService(CreateClient(response));
        var result = await service.SearchProductAsync("Banane");

        Assert.NotNull(result);
        Assert.Equal("Banane", result!.ProductName);
        Assert.Equal("Bio", result.Brands);
        Assert.Equal("1 kg", result.Quantity);
        Assert.Equal("a", result.NutriScoreGrade);
        Assert.Equal("https://world.openfoodfacts.org/product/123", result.Url);
        Assert.NotNull(result.Nutriments);
        Assert.Equal(89, result.Nutriments.EnergyKcal);
        Assert.Equal(1.1, result.Nutriments.Proteins);
        Assert.Equal(0.358, result.Nutriments.Potassium);
    }

    [Fact]
    public async Task SearchProductAsync_ReturnsNull_WhenNoProductsHaveNames()
    {
        var json = @"{""count"":1,""page"":1,""products"":[{""product_name"":"""",""url"":""https://example.com"",""nutriments"":{}}]}";
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json)
        };

        var service = CreateService(CreateClient(response));
        var result = await service.SearchProductAsync("xyz");

        Assert.Null(result);
    }

    [Fact]
    public async Task SearchProductAsync_ReturnsNull_WhenResponseIsEmpty()
    {
        var json = @"{""count"":0,""page"":1,""products"":[]}";
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json)
        };

        var service = CreateService(CreateClient(response));
        var result = await service.SearchProductAsync("unknown");

        Assert.Null(result);
    }

    [Fact]
    public async Task SearchProductAsync_ReturnsNull_WhenRequestFails()
    {
        var response = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        var service = CreateService(CreateClient(response));

        var result = await service.SearchProductAsync("Banane");

        Assert.Null(result);
    }

    [Fact]
    public async Task SearchProductAsync_ReturnsNull_ForBlankQuery()
    {
        var service = CreateService(new HttpClient());
        var result = await service.SearchProductAsync("   ");
        Assert.Null(result);
    }

    [Fact]
    public void Constructor_Throws_WhenDependenciesAreNull()
    {
        Assert.Throws<ArgumentNullException>(() => new OpenFoodFactsService(null!, NullLogger<OpenFoodFactsService>.Instance, new MemoryCache(new MemoryCacheOptions())));
        Assert.Throws<ArgumentNullException>(() => new OpenFoodFactsService(new HttpClient(), null!, new MemoryCache(new MemoryCacheOptions())));
        Assert.Throws<ArgumentNullException>(() => new OpenFoodFactsService(new HttpClient(), NullLogger<OpenFoodFactsService>.Instance, null!));
    }
}
