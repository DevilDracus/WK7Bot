using WK7Bot.Core.Entities;
using WK7Bot.Modules;
using Xunit;

namespace WK7Bot.Tests;

public class PriceHistoryChartRendererTests
{
    [Fact]
    public void Render_ReturnsNonEmptyString()
    {
        var snapshots = new List<PriceHistorySnapshot>
        {
            new() { AppId = 1, Store = "Steam", DiscountPercent = 0, RecordedAt = DateTime.UtcNow.AddDays(-3) },
            new() { AppId = 1, Store = "Steam", DiscountPercent = 50, RecordedAt = DateTime.UtcNow.AddDays(-2) },
            new() { AppId = 1, Store = "Steam", DiscountPercent = 25, RecordedAt = DateTime.UtcNow.AddDays(-1) }
        };

        var chart = PriceHistoryChartRenderer.Render(snapshots);

        Assert.NotNull(chart);
        Assert.NotEmpty(chart);
        Assert.Contains('█', chart);
    }

    [Fact]
    public void Render_ReturnsNoData_WhenEmpty()
    {
        var chart = PriceHistoryChartRenderer.Render(Array.Empty<PriceHistorySnapshot>());
        Assert.Contains("Keine Daten", chart);
    }
}
