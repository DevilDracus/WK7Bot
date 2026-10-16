using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using WK7Bot.Core.Utilities;
using WK7Bot.Options;
using WK7Bot.Services;
using WK7Bot.Services.Interfaces;
using Xunit;

namespace WK7Bot.Tests;

public class FoodPublisherServiceTests
{
    private sealed class TestFoodPublisherService : FoodPublisherService
    {
        public TestFoodPublisherService(
            DiscordSocketClient discordClient,
            IGeminiFoodService geminiFoodService,
            IRecipeSearchService recipeSearchService,
            IOptions<Wk7BotOptions> options,
            FeatureHealthTracker healthTracker,
            ILoggerFactory loggerFactory,
            IServiceScopeFactory scopeFactory)
            : base(
                discordClient,
                geminiFoodService,
                recipeSearchService,
                options,
                scopeFactory,
                healthTracker,
                NullLogger<FoodPublisherService>.Instance,
                timeProvider: TimeProvider.System)
        {
            // The logger factory is intentionally not used; NullLogger keeps test output quiet.
            _ = loggerFactory;
        }

        public bool MonthlyProduceDue(DateTime now) => ShouldPublishMonthlyProduce(now);

        public bool WeeklyRecipeDue(DateTime now) => ShouldPublishWeeklyRecipe(now);
    }

    private static TestFoodPublisherService CreateService()
    {
        return new TestFoodPublisherService(
            new DiscordSocketClient(),
            Mock.Of<IGeminiFoodService>(),
            Mock.Of<IRecipeSearchService>(),
            Microsoft.Extensions.Options.Options.Create(new Wk7BotOptions()),
            new FeatureHealthTracker(),
            NullLoggerFactory.Instance,
            Mock.Of<IServiceScopeFactory>());
    }

    [Fact]
    public void ShouldPublishMonthlyProduce_ReturnsTrue_WhenFirstDayAfterWindowOpens()
    {
        var service = CreateService();

        var due = service.MonthlyProduceDue(new DateTime(2026, 10, 1, 9, 0, 0));

        Assert.True(due);
    }

    [Fact]
    public void ShouldPublishMonthlyProduce_ReturnsFalse_WhenBeforeWindowOpens()
    {
        var service = CreateService();

        var due = service.MonthlyProduceDue(new DateTime(2026, 10, 1, 8, 59, 0));

        Assert.False(due);
    }

    [Fact]
    public void ShouldPublishMonthlyProduce_ReturnsFalse_WhenNotFirstDayOfMonth()
    {
        var service = CreateService();

        var due = service.MonthlyProduceDue(new DateTime(2026, 10, 2, 9, 0, 0));

        Assert.False(due);
    }

    [Fact]
    public void ShouldPublishWeeklyRecipe_ReturnsTrue_WhenThursdayAfterWindowOpens()
    {
        var service = CreateService();

        var due = service.WeeklyRecipeDue(new DateTime(2026, 10, 15, 15, 30, 0));

        Assert.True(due);
    }

    [Fact]
    public void ShouldPublishWeeklyRecipe_ReturnsFalse_WhenBeforeWindowOpens()
    {
        var service = CreateService();

        var due = service.WeeklyRecipeDue(new DateTime(2026, 10, 15, 15, 29, 0));

        Assert.False(due);
    }

    [Fact]
    public void ShouldPublishWeeklyRecipe_ReturnsFalse_WhenNotThursday()
    {
        var service = CreateService();

        var due = service.WeeklyRecipeDue(new DateTime(2026, 10, 16, 15, 30, 0));

        Assert.False(due);
    }
}
