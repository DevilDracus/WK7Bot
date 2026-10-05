using WK7Bot.Core.Utilities;
using Xunit;

namespace WK7Bot.Tests;

public class FeedDeltaCalculatorTests
{
    [Fact]
    public void HasBaseline_False_WhenBothMarkersMissing()
    {
        Assert.False(FeedDeltaCalculator.HasBaseline(null, null));
        Assert.False(FeedDeltaCalculator.HasBaseline("", null));
    }

    [Fact]
    public void HasBaseline_True_WhenGuidPresent()
    {
        Assert.True(FeedDeltaCalculator.HasBaseline("abc", null));
    }

    [Fact]
    public void HasBaseline_True_WhenDatePresent()
    {
        Assert.True(FeedDeltaCalculator.HasBaseline(null, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void IsItemNewer_False_WhenGuidMatchesLast()
    {
        var now = DateTimeOffset.UtcNow;
        Assert.False(FeedDeltaCalculator.IsItemNewer("same", now, "same", now.AddDays(-1)));
    }

    [Fact]
    public void IsItemNewer_True_WhenItemPublishedAfterBaseline()
    {
        var baseline = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);
        var newer = baseline.AddHours(1);

        Assert.True(FeedDeltaCalculator.IsItemNewer("b", newer, "a", baseline));
    }

    [Fact]
    public void IsItemNewer_False_WhenItemPublishedBeforeBaseline()
    {
        var baseline = new DateTimeOffset(2026, 9, 10, 0, 0, 0, TimeSpan.Zero);
        var older = baseline.AddDays(-3);

        Assert.False(FeedDeltaCalculator.IsItemNewer("b", older, "a", baseline));
    }

    [Fact]
    public void IsItemNewer_False_WhenTimestampsEqual()
    {
        var ts = new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);

        Assert.False(FeedDeltaCalculator.IsItemNewer("b", ts, "a", ts));
    }

    [Fact]
    public void IsItemNewer_True_WhenNoDateBaseline_ButGuidDiffers()
    {
        Assert.True(FeedDeltaCalculator.IsItemNewer("item-2", null, "item-1", null));
    }

    [Fact]
    public void IsItemNewer_False_WhenSameGuid_EvenWithoutDateBaseline()
    {
        Assert.False(FeedDeltaCalculator.IsItemNewer("item-1", null, "item-1", null));
    }

    [Fact]
    public void IsItemNewer_ComparesAbsoluteTime_RegardlessOfOffset()
    {
        var utcBaseline = new DateTimeOffset(2026, 9, 1, 10, 0, 0, TimeSpan.Zero);
        var localLater = new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.FromHours(2)); // == 10:00 UTC

        // Equal absolute moment → not newer
        Assert.False(FeedDeltaCalculator.IsItemNewer("x", localLater, "y", utcBaseline));

        var trulyLater = new DateTimeOffset(2026, 9, 1, 13, 0, 0, TimeSpan.FromHours(2)); // == 11:00 UTC
        Assert.True(FeedDeltaCalculator.IsItemNewer("x", trulyLater, "y", utcBaseline));
    }

    [Fact]
    public void SelectNewestForBaseline_ReturnsNull_WhenEmpty()
    {
        Assert.Null(FeedDeltaCalculator.SelectNewestForBaseline(new List<string>(), _ => null));
    }

    [Fact]
    public void SelectNewestForBaseline_ReturnsOnlyItem_WhenSingle()
    {
        var items = new List<string> { "only" };
        var result = FeedDeltaCalculator.SelectNewestForBaseline(items, _ => DateTimeOffset.UtcNow);
        Assert.Equal("only", result);
    }

    [Fact]
    public void SelectNewestForBaseline_PicksLatestTimestamp()
    {
        var d1 = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var d2 = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
        var d3 = new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero);

        var items = new List<TestItem>
        {
            new("a", d1), new("b", d2), new("c", d3)
        };

        var result = FeedDeltaCalculator.SelectNewestForBaseline(items, i => i.Date);

        Assert.NotNull(result);
        Assert.Equal("b", result!.Id);
    }

    private sealed record TestItem(string Id, DateTimeOffset Date);
}
