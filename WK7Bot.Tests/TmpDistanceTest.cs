using WK7Bot.Core.Utilities;
using WK7Bot.Models;
using Xunit;

namespace WK7Bot.Tests;

public class TmpDistanceTest
{
    [Fact]
    public void FarCentroid_IsFiltered()
    {
        var pts = new List<GeoPoint>
        {
            new(49.0, 11.0),
            new(49.0, 13.0),
            new(52.0, 13.0),
            new(52.0, 11.0),
            new(49.0, 11.0)
        };
        var w = new CapWarning { Polygons = new List<IReadOnlyList<GeoPoint>> { pts } };
        Assert.True(CapWarningParser.PointInPolygon(pts, 51.34, 12.36));
        Assert.False(CapWarningParser.MatchesLocation(w, 51.34, 12.36, Array.Empty<string>(), 75));
    }

    [Fact]
    public void NearCentroid_IsAllowed()
    {
        var pts = new List<GeoPoint>
        {
            new(51.30, 12.30),
            new(51.30, 12.40),
            new(51.40, 12.40),
            new(51.40, 12.30),
            new(51.30, 12.30)
        };
        var w = new CapWarning { Polygons = new List<IReadOnlyList<GeoPoint>> { pts } };
        Assert.True(CapWarningParser.MatchesLocation(w, 51.34, 12.36, Array.Empty<string>(), 75));
    }
}
