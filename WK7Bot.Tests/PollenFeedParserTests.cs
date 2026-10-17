using WK7Bot.Core.Utilities;
using Xunit;

namespace WK7Bot.Tests;

public class PollenFeedParserTests
{
    private const string PollenJson = """
        {
          "name": "Pollenflug-Gefahrenindex",
          "last_update": "2026-10-17 11:00 Uhr",
          "next_update": "2026-10-18 11:00 Uhr",
          "legend": {
            "id1": "0", "id1_desc": "keine Belastung",
            "id2": "0-1", "id2_desc": "keine bis geringe Belastung",
            "id3": "1", "id3_desc": "geringe Belastung",
            "id4": "1-2", "id4_desc": "geringe bis mittlere Belastung",
            "id5": "2", "id5_desc": "mittlere Belastung",
            "id6": "2-3", "id6_desc": "mittlere bis hohe Belastung",
            "id7": "3", "id7_desc": "hohe Belastung"
          },
          "content": [
            {
              "region_id": 60, "partregion_id": 61, "region_name": "Sachsen-Anhalt", "partregion_name": "Tiefland Sachsen-Anhalt",
              "Pollen": { "Hasel": { "today": "0", "tomorrow": "0", "dayafter_to": "0" } }
            },
            {
              "region_id": 80, "partregion_id": 81, "region_name": "Sachsen", "partregion_name": "Tiefland Sachsen",
              "Pollen": {
                "Hasel": { "today": "0", "tomorrow": "0", "dayafter_to": "0" },
                "Erle": { "today": "0", "tomorrow": "0", "dayafter_to": "0" },
                "Esche": { "today": "0", "tomorrow": "0", "dayafter_to": "0" },
                "Birke": { "today": "0-1", "tomorrow": "1", "dayafter_to": "1-2" },
                "Graeser": { "today": "2-3", "tomorrow": "2", "dayafter_to": "-1" },
                "Roggen": { "today": "0", "tomorrow": "0", "dayafter_to": "0" },
                "Beifuss": { "today": "0", "tomorrow": "0", "dayafter_to": "0" },
                "Ambrosia": { "today": "0", "tomorrow": "0", "dayafter_to": "0" }
              }
            },
            {
              "region_id": 80, "partregion_id": 82, "region_name": "Sachsen", "partregion_name": "Mittelgebirge Sachsen",
              "Pollen": { "Hasel": { "today": "0", "tomorrow": "0", "dayafter_to": "0" } }
            }
          ]
        }
        """;

    [Fact]
    public void Parse_SelectsThePartRegion()
    {
        var forecast = PollenFeedParser.Parse(PollenJson, "Sachsen", "Tiefland Sachsen");

        Assert.NotNull(forecast);
        Assert.Equal("Sachsen", forecast!.RegionName);
        Assert.Equal("Tiefland Sachsen", forecast.PartRegionName);
        Assert.Equal(8, forecast.Types.Count);
    }

    [Fact]
    public void Parse_ResolvesLegendDescriptions()
    {
        var forecast = PollenFeedParser.Parse(PollenJson, "Sachsen", "Tiefland Sachsen")!;

        var grasses = forecast.Types.Single(type => type.Name == "Gräser");
        Assert.Equal("2-3", grasses.Today.Raw);
        Assert.Equal(2.5, grasses.Today.Value);
        Assert.Equal("mittlere bis hohe Belastung", grasses.Today.Description);
        Assert.True(grasses.Today.HasLoad);

        Assert.True(grasses.DayAfterTomorrow.IsMissing);
        Assert.False(grasses.DayAfterTomorrow.HasLoad);
    }

    [Fact]
    public void Parse_MapsAsciiPollenKeysToDisplayNames()
    {
        var forecast = PollenFeedParser.Parse(PollenJson, "Sachsen", "Tiefland Sachsen")!;

        Assert.Contains(forecast.Types, type => type.Name == "Gräser");
        Assert.Contains(forecast.Types, type => type.Name == "Beifuß");
    }

    [Fact]
    public void Parse_UnknownRegion_ReturnsNull()
    {
        Assert.Null(PollenFeedParser.Parse(PollenJson, "Nordpol", "Iglu"));
    }

    [Fact]
    public void Parse_EmptyPayload_ReturnsNull()
    {
        Assert.Null(PollenFeedParser.Parse(string.Empty, "Sachsen", "Tiefland Sachsen"));
    }

    [Fact]
    public void ParseUv_ExtractsCityForecast()
    {
        const string uvJson = """
            {
              "name": "UV-Gefahrenindex",
              "last_update": "2026-10-17T07:30:00",
              "content": [
                { "city": "Magdeburg", "forecast": { "today": 1, "tomorrow": 1, "dayafter_to": 1 } },
                { "city": "Leipzig", "forecast": { "today": 2, "tomorrow": 3, "dayafter_to": 1 } }
              ]
            }
            """;

        var forecast = PollenFeedParser.ParseUv(uvJson, "Leipzig");

        Assert.NotNull(forecast);
        Assert.Equal("Leipzig", forecast!.City);
        Assert.Equal(2, forecast.Today);
        Assert.Equal(3, forecast.Tomorrow);
        Assert.Equal(1, forecast.DayAfterTomorrow);
    }

    [Fact]
    public void ParseUv_UnknownCity_ReturnsNull()
    {
        Assert.Null(PollenFeedParser.ParseUv("{\"content\":[]}", "Leipzig"));
    }
}
