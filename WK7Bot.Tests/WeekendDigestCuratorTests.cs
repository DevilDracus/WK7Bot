using WK7Bot.Core.Utilities;
using WK7Bot.Models;
using Xunit;

namespace WK7Bot.Tests;

public class WeekendDigestCuratorTests
{
    private static readonly DateTime Friday = new(2026, 10, 9);
    private static readonly DateTime Saturday = new(2026, 10, 10);
    private static readonly DateTime Sunday = new(2026, 10, 11);

    private static WeekendEvent Event(
        string title,
        DateTime date,
        string? location = null,
        string? topic = null,
        string? url = null)
        => new()
        {
            Title = title,
            StartDate = date,
            EndDate = date,
            Location = location,
            Topic = topic,
            Url = url ?? $"https://example.test/{Guid.NewGuid():N}"
        };

    [Fact]
    public void PickSuggestions_PrefersMarketStyleTitles()
    {
        var candidates = new[]
        {
            Event("Konzert im Gewandhaus", Saturday, "Gewandhaus", url: "https://example.test/konzert"),
            Event("Wochenmarkt in der Innenstadt", Saturday, "Richard-Wagner-Platz", url: "https://example.test/markt")
        };

        var picks = WeekendDigestCurator.PickSuggestions(candidates, Friday);

        Assert.Equal(2, picks.Count);
        Assert.Equal("Wochenmarkt in der Innenstadt", picks[0].Title);
        Assert.Equal("Konzert im Gewandhaus", picks[1].Title);
    }

    [Fact]
    public void PickSuggestions_SpreadsOnePickPerDay_WhenPossible()
    {
        var candidates = new[]
        {
            Event("Konzert", Saturday, "Gewandhaus"),
            Event("Kino im Zoo", Sunday, "Zoo"),
            Event("Spaziergang am Auwald", Friday, "Auwald"),
            Event("Wochenmarkt", Saturday, "Augustusplatz"),
            Event("Flohmarkt", Sunday, "Leuthof")
        };

        var picks = WeekendDigestCurator.PickSuggestions(candidates, Friday);

        Assert.Equal(3, picks.Count);
        Assert.Equal(Friday, picks[0].StartDate);
        Assert.Equal(Saturday, picks[1].StartDate);
        Assert.Equal(Sunday, picks[2].StartDate);
        Assert.Equal("Wochenmarkt", picks[1].Title);
        Assert.Equal("Flohmarkt", picks[2].Title);
    }

    [Fact]
    public void PickSuggestions_NeverRepeatsTheSameVenue()
    {
        var candidates = new[]
        {
            Event("Konzert", Friday, "Gewandhaus"),
            Event("Lesung", Saturday, "Gewandhaus"),
            Event("Theater", Sunday, "Schauspiel"),
            Event("Oper", Saturday, "Oper")
        };

        var picks = WeekendDigestCurator.PickSuggestions(candidates, Friday);

        Assert.Equal(3, picks.Count);
        Assert.DoesNotContain(picks, p => p.Title == "Lesung");
        var venues = picks.Select(p => p.Location).ToList();
        Assert.Equal(venues.Count, venues.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void PickSuggestions_FillsFromRemainingDays_WhenADayHasNoEvents()
    {
        var candidates = new[]
        {
            Event("Wochenmarkt", Saturday, "Augustusplatz"),
            Event("Flohmarkt", Sunday, "Leuthof")
        };

        var picks = WeekendDigestCurator.PickSuggestions(candidates, Friday);

        Assert.Equal(2, picks.Count);
        Assert.Equal(Saturday, picks[0].StartDate);
        Assert.Equal(Sunday, picks[1].StartDate);
    }

    [Fact]
    public void PickSuggestions_IgnoresEventsOutsideTheWeekend()
    {
        var candidates = new[]
        {
            Event("Markt der Vorwoche", new DateTime(2026, 10, 2), "Zentrum"),
            Event("Wochenmarkt", Saturday, "Augustusplatz"),
            Event("Markt nächsten Freitag", new DateTime(2026, 10, 16), "Zentrum")
        };

        var picks = WeekendDigestCurator.PickSuggestions(candidates, Friday);

        var pick = Assert.Single(picks);
        Assert.Equal("Wochenmarkt", pick.Title);
    }

    [Fact]
    public void PickSuggestions_IncludesMultiDayEventsOverlappingTheWeekend()
    {
        var multiDay = Event("Europacup im Speedklettern", new DateTime(2026, 10, 10));
        multiDay.EndDate = new DateTime(2026, 10, 12);

        var picks = WeekendDigestCurator.PickSuggestions(new[] { multiDay }, Friday);

        Assert.Single(picks);
    }

    [Fact]
    public void PickSuggestions_ReturnsFewerThanRequested_WhenCandidatesRunOut()
    {
        var candidates = new[] { Event("Wochenmarkt", Saturday, "Augustusplatz") };

        var picks = WeekendDigestCurator.PickSuggestions(candidates, Friday);

        Assert.Single(picks);
    }

    [Fact]
    public void PickSuggestions_ReturnsEmpty_ForNoCandidatesOrInvalidCount()
    {
        Assert.Empty(WeekendDigestCurator.PickSuggestions(Array.Empty<WeekendEvent>(), Friday));
        Assert.Empty(WeekendDigestCurator.PickSuggestions(
            new[] { Event("Wochenmarkt", Saturday) }, Friday, 0));
    }

    [Fact]
    public void PickSuggestions_TopicBonus_BreaksTiesBetweenCultureEvents()
    {
        var candidates = new[]
        {
            Event("Stadtführung", Saturday, "Altstadt", topic: "Messen"),
            Event("Konzert", Saturday, "Gewandhaus")
        };

        var picks = WeekendDigestCurator.PickSuggestions(candidates, Friday);

        Assert.Equal(2, picks.Count);
        Assert.Equal("Stadtführung", picks[0].Title);
    }
}
