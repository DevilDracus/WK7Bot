using System.Xml;
using WK7Bot.Core.Utilities;
using WK7Bot.Models;
using Xunit;

namespace WK7Bot.Tests;

public class CapWarningParserTests
{
    private const string StarkregenAlert =
        """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <alert xmlns="urn:oasis:names:tc:emergency:cap:1.2">
          <identifier>2.49.0.0.276.0.DWD.PVW.1791255600000.test-uuid.DEU</identifier>
          <sender>opendata@dwd.de</sender>
          <sent>2026-10-06T13:15:00+02:00</sent>
          <status>Actual</status>
          <msgType>Alert</msgType>
          <source>PVW</source>
          <scope>Public</scope>
          <info>
            <language>de-DE</language>
            <category>Met</category>
            <event>STARKREGEN</event>
            <urgency>Immediate</urgency>
            <severity>Severe</severity>
            <certainty>Likely</certainty>
            <eventCode>
              <valueName>GROUP</valueName>
              <value>HEAVY RAIN</value>
            </eventCode>
            <eventCode>
              <valueName>AREA_COLOR</valueName>
              <value>255 87 34</value>
            </eventCode>
            <effective>2026-10-06T13:15:00+02:00</effective>
            <onset>2026-10-06T14:00:00+02:00</onset>
            <expires>2026-10-06T16:00:00+02:00</expires>
            <senderName>Deutscher Wetterdienst</senderName>
            <headline>Amtliche WARNUNG vor STARKREGEN</headline>
            <description>Es tritt starker Regen auf.</description>
            <instruction>Balkonfenster schließen.</instruction>
            <web>https://dwd.de/warnungen</web>
            <area>
              <areaDesc>polygonal event area</areaDesc>
              <polygon>51.30,12.30 51.30,12.40 51.40,12.40 51.40,12.30 51.30,12.30</polygon>
              <geocode>
                <valueName>EXCLUDE_POLYGON</valueName>
                <value>51.34,12.34 51.34,12.35 51.35,12.35 51.35,12.34 51.34,12.34</value>
              </geocode>
            </area>
            <area>
              <areaDesc>Stadt Leipzig</areaDesc>
              <geocode>
                <valueName>WARNCELLID</valueName>
                <value>805111000</value>
              </geocode>
            </area>
          </info>
        </alert>
        """;

    private const string CellOnlyAlert =
        """
        <alert xmlns="urn:oasis:names:tc:emergency:cap:1.2">
          <identifier>cell-warning-id</identifier>
          <sent>2026-10-06T13:15:00+02:00</sent>
          <info>
            <event>STURM</event>
            <severity>Moderate</severity>
            <onset>2026-10-06T14:00:00+02:00</onset>
            <expires>2026-10-06T16:00:00+02:00</expires>
            <headline>Amtliche WARNUNG vor STURM</headline>
            <area>
              <areaDesc>Stadt Leipzig</areaDesc>
            </area>
          </info>
        </alert>
        """;

    private static CapWarning Parse(string xml)
        => CapWarningParser.ParseAlert(xml).Single();

    private static CapWarning WarningWithoutPolygon()
    {
        var warning = Parse(CellOnlyAlert);
        return warning;
    }

    [Fact]
    public void ParseAlert_ExtractsAllRelevantFields()
    {
        var warning = Parse(StarkregenAlert);

        Assert.Equal("2.49.0.0.276.0.DWD.PVW.1791255600000.test-uuid.DEU", warning.Identifier);
        Assert.Equal("STARKREGEN", warning.Event);
        Assert.Equal("HEAVY RAIN", warning.EventGroup);
        Assert.Equal("Severe", warning.Severity);
        Assert.Equal("Amtliche WARNUNG vor STARKREGEN", warning.Headline);
        Assert.Equal("Es tritt starker Regen auf.", warning.Description);
        Assert.Equal("Balkonfenster schließen.", warning.Instruction);
        Assert.Equal("https://dwd.de/warnungen", warning.Web);
        Assert.Equal(new[] { "polygonal event area", "Stadt Leipzig" }, warning.AreaDescriptions);
        Assert.Single(warning.Polygons);
        Assert.Single(warning.ExcludePolygons);
    }

    [Fact]
    public void ParseAlert_ConvertsOffsetTimesToLocalTime()
    {
        var warning = Parse(StarkregenAlert);

        Assert.Equal(new DateTime(2026, 10, 6, 12, 0, 0, DateTimeKind.Utc), warning.Onset.ToUniversalTime());
        Assert.Equal(new DateTime(2026, 10, 6, 14, 0, 0, DateTimeKind.Utc), warning.Expires.ToUniversalTime());
    }

    [Fact]
    public void ParseAlert_FallsBackToEffective_WhenOnsetMissing()
    {
        var warning = Parse(
            """
            <alert xmlns="urn:oasis:names:tc:emergency:cap:1.2">
              <identifier>fallback-id</identifier>
              <sent>2026-10-06T13:15:00+02:00</sent>
              <info>
                <event>NEBEL</event>
                <effective>2026-10-06T14:30:00+02:00</effective>
                <expires>2026-10-06T16:00:00+02:00</expires>
              </info>
            </alert>
            """);

        Assert.Equal(new DateTime(2026, 10, 6, 12, 30, 0, DateTimeKind.Utc), warning.Onset.ToUniversalTime());
    }

    [Fact]
    public void ParseAlert_UsesMaxValue_WhenExpiresMissing()
    {
        var warning = Parse(
            """
            <alert xmlns="urn:oasis:names:tc:emergency:cap:1.2">
              <identifier>no-expiry-id</identifier>
              <sent>2026-10-06T13:15:00+02:00</sent>
              <info>
                <event>NEBEL</event>
              </info>
            </alert>
            """);

        Assert.Equal(DateTime.MaxValue, warning.Expires);
    }

    [Fact]
    public void ParseAlert_SuffixesIdentifiers_WhenAlertCarriesMultipleInfos()
    {
        var warnings = CapWarningParser.ParseAlert(
            """
            <alert xmlns="urn:oasis:names:tc:emergency:cap:1.2">
              <identifier>multi-id</identifier>
              <sent>2026-10-06T13:15:00+02:00</sent>
              <info>
                <event>STARKREGEN</event>
              </info>
              <info>
                <event>HAGEL</event>
              </info>
            </alert>
            """);

        Assert.Equal(2, warnings.Count);
        Assert.Equal("multi-id#1", warnings[0].Identifier);
        Assert.Equal("multi-id#2", warnings[1].Identifier);
    }

    [Fact]
    public void ParseAlert_MalformedXml_ThrowsXmlException()
    {
        Assert.Throws<XmlException>(() => CapWarningParser.ParseAlert("<alert><info></alert>"));
    }

    [Fact]
    public void ParsePolygon_ReadsLatitudeFirst_AndSkipsGarbage()
    {
        var points = CapWarningParser.ParsePolygon("51.30,12.30 51.40,12.40 oops 51.35,12.35,7");

        Assert.Equal(2, points.Count);
        Assert.Equal(51.30, points[0].Latitude, 6);
        Assert.Equal(12.30, points[0].Longitude, 6);
        Assert.Equal(51.40, points[1].Latitude, 6);
        Assert.Equal(12.40, points[1].Longitude, 6);
    }

    [Fact]
    public void PointInPolygon_DetectsInsideAndOutside()
    {
        var polygon = CapWarningParser.ParsePolygon("51.30,12.30 51.30,12.40 51.40,12.40 51.40,12.30 51.30,12.30");

        Assert.True(CapWarningParser.PointInPolygon(polygon, 51.35, 12.35));
        Assert.False(CapWarningParser.PointInPolygon(polygon, 51.50, 12.50));
        Assert.False(CapWarningParser.PointInPolygon(polygon, 51.35, 12.20));
        Assert.False(CapWarningParser.PointInPolygon(polygon, 51.45, 12.35));
    }

    [Fact]
    public void PointInPolygon_TooFewPoints_IsNeverInside()
    {
        var polygon = new List<GeoPoint> { new(51.0, 12.0), new(52.0, 13.0) };

        Assert.False(CapWarningParser.PointInPolygon(polygon, 51.5, 12.5));
    }

    [Fact]
    public void MatchesLocation_InsidePolygon_ReturnsTrue()
    {
        var warning = Parse(StarkregenAlert);

        Assert.True(CapWarningParser.MatchesLocation(warning, 51.38, 12.38, new[] { "Stadt Leipzig" }));
    }

    [Fact]
    public void MatchesLocation_InsideExcludePolygon_IsVetoed()
    {
        var warning = Parse(StarkregenAlert);

        Assert.False(CapWarningParser.MatchesLocation(warning, 51.345, 12.345, new[] { "Stadt Leipzig" }));
    }

    [Fact]
    public void MatchesLocation_OutsidePolygon_IsFalse_EvenWhenAreaNameMatches()
    {
        var warning = Parse(StarkregenAlert);

        Assert.False(CapWarningParser.MatchesLocation(warning, 51.50, 12.50, new[] { "Stadt Leipzig" }));
    }

    [Fact]
    public void MatchesLocation_FallsBackToAreaName_WhenWarningHasNoPolygon()
    {
        var warning = WarningWithoutPolygon();

        Assert.True(CapWarningParser.MatchesLocation(warning, 51.34, 12.36, new[] { "Stadt Leipzig" }));
        Assert.False(CapWarningParser.MatchesLocation(warning, 51.34, 12.36, new[] { "Stadt Gohlis" }));
        Assert.False(CapWarningParser.MatchesLocation(warning, 51.34, 12.36, Array.Empty<string>()));
    }

    [Fact]
    public void MatchesLocation_AreaNameComparison_IgnoresCaseAndWhitespace()
    {
        var warning = WarningWithoutPolygon();

        Assert.True(CapWarningParser.MatchesLocation(warning, 51.34, 12.36, new[] { "  stadt leipzig " }));
    }

    [Fact]
    public void MatchesEvent_MatchesGermanEventName()
    {
        var warning = Parse(StarkregenAlert);

        Assert.True(CapWarningParser.MatchesEvent(warning, new[] { "STARKREGEN" }));
        Assert.False(CapWarningParser.MatchesEvent(warning, new[] { "HAGEL" }));
    }

    [Fact]
    public void MatchesEvent_MatchesEnglishGroup_WithUnderscoreSeparator()
    {
        var warning = Parse(StarkregenAlert);

        Assert.True(CapWarningParser.MatchesEvent(warning, new[] { "HEAVY_RAIN" }));
        Assert.True(CapWarningParser.MatchesEvent(warning, new[] { "heavy rain" }));
    }

    [Fact]
    public void MatchesEvent_NormalizesUmlauts()
    {
        var warning = new CapWarning { Event = "BÖEN", EventGroup = "WIND GUSTS" };

        Assert.True(CapWarningParser.MatchesEvent(warning, new[] { "BOEEN" }));
        Assert.True(CapWarningParser.MatchesEvent(warning, new[] { "BÖEN" }));
    }

    [Fact]
    public void MatchesEvent_NoMatchForUnrelatedWarning()
    {
        var warning = WarningWithoutPolygon();
        warning.Event = "NEBEL";
        warning.EventGroup = "FOG";

        Assert.False(CapWarningParser.MatchesEvent(warning, new[] { "STARKREGEN", "HAGEL", "STORM" }));
    }

    [Fact]
    public void MatchesEvent_EmptyTokenList_NeverMatches()
    {
        var warning = Parse(StarkregenAlert);

        Assert.False(CapWarningParser.MatchesEvent(warning, Array.Empty<string>()));
        Assert.False(CapWarningParser.MatchesEvent(warning, new[] { " " }));
    }
}
