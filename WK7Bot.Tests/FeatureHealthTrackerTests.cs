using WK7Bot.Core.Utilities;
using Xunit;

namespace WK7Bot.Tests;

public class FeatureHealthTrackerTests
{
    [Fact]
    public void RecordSuccess_ClearsLastErrorAfterFailure()
    {
        var tracker = new FeatureHealthTracker();

        tracker.RecordFailure("pollen", "boom");
        Assert.Equal("boom", tracker.Get("pollen").LastError);

        tracker.RecordSuccess("pollen");
        Assert.Null(tracker.Get("pollen").LastError);
    }

    [Fact]
    public void RecordSuccess_MarksFeatureNotProblem()
    {
        var tracker = new FeatureHealthTracker();

        tracker.RecordFailure("pollen", "boom");
        Assert.True(tracker.Get("pollen").IsProblem);

        tracker.RecordSuccess("pollen");
        Assert.False(tracker.Get("pollen").IsProblem);
    }

    [Fact]
    public void RecordFailure_SetsLastErrorAndIsProblem()
    {
        var tracker = new FeatureHealthTracker();

        tracker.RecordFailure("rss", "timeout");

        var snapshot = tracker.Get("rss");
        Assert.Equal("timeout", snapshot.LastError);
        Assert.True(snapshot.IsProblem);
        Assert.True(snapshot.HasRun);
    }

    [Fact]
    public void Get_UnknownFeature_ReturnsNeverRunSnapshot()
    {
        var tracker = new FeatureHealthTracker();

        var snapshot = tracker.Get("never-registered");

        Assert.False(snapshot.HasRun);
        Assert.False(snapshot.IsProblem);
        Assert.Null(snapshot.LastError);
    }

    [Fact]
    public void FeatureKeys_AreCaseInsensitive()
    {
        var tracker = new FeatureHealthTracker();

        tracker.RecordFailure("Pollen", "err");
        tracker.RecordSuccess("pollen");

        Assert.Null(tracker.Get("POLLEN").LastError);
        Assert.False(tracker.Get("POLLEN").IsProblem);
    }

    [Fact]
    public void GetAll_ReturnsCopyOfAllTrackedFeatures()
    {
        var tracker = new FeatureHealthTracker();

        tracker.RecordSuccess("a");
        tracker.RecordFailure("b", "x");

        var all = tracker.GetAll();

        Assert.Equal(2, all.Count);
        Assert.True(all.ContainsKey("a"));
        Assert.True(all.ContainsKey("b"));
    }

    [Fact]
    public void RecordSuccess_NullFeature_Throws()
    {
        var tracker = new FeatureHealthTracker();

        Assert.Throws<ArgumentNullException>(() => tracker.RecordSuccess(null!));
    }

    [Fact]
    public void RecordFailure_NullFeature_Throws()
    {
        var tracker = new FeatureHealthTracker();

        Assert.Throws<ArgumentNullException>(() => tracker.RecordFailure(null!));
    }
}
