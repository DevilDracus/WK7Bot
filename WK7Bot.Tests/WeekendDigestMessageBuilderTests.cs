using Discord;
using WK7Bot.Core.Utilities;
using WK7Bot.Models;
using Xunit;

namespace WK7Bot.Tests;

public class WeekendDigestMessageBuilderTests
{
    private static readonly DateTime Friday = new(2026, 10, 9);
    private static readonly DateTime Sunday = new(2026, 10, 11);
    private static readonly DateTime PostTime = new(2026, 10, 8, 14, 15, 0);

    private static WeekendEvent Event(
        string title,
        DateTime date,
        string timeText = "10:00 – 16:00 Uhr",
        string location = "Augustusplatz",
        string? topic = null)
        => new()
        {
            Title = title,
            StartDate = date,
            EndDate = date,
            TimeText = timeText,
            Location = location,
            Topic = topic,
            Url = $"https://www.leipzig.de/kultur-und-freizeit/veranstaltungen/eventsingle/event/{title.GetHashCode():x}"
        };

    [Fact]
    public void Build_ThreeSuggestions_RendersEmbedAndPoll()
    {
        var suggestions = new[]
        {
            Event("Wochenmarkt in der Innenstadt", Friday, "09:00 – 17:40 Uhr", "Augustusplatz", "Messen"),
            Event("Flohmarkt Leuthof", new DateTime(2026, 10, 10)),
            Event("Straßenmarkt Süd", Sunday, "16:00 – 22:00 Uhr", "Südplatz")
        };

        var (embed, poll) = WeekendDigestMessageBuilder.Build(suggestions, Friday, PostTime);

        Assert.Equal("📅 Wochenend-Tipps — 09.–11. Oktober 2026", embed.Title);
        Assert.Equal("**3 Tipps** für das Wochenende – stimmt ab, wohin wir gehen! 🗳️", embed.Description);
        Assert.Equal("Datenquelle: leipzig.de", embed.Footer?.Text);
        Assert.Equal(PostTime, embed.Timestamp!.Value.DateTime);

        Assert.Equal(3, embed.Fields.Length);
        Assert.StartsWith("1️⃣ Fr, 09.10. · Wochenmarkt in der Innenstadt", embed.Fields[0].Name);
        Assert.Contains("⏰ 09:00 – 17:40 Uhr", embed.Fields[0].Value);
        Assert.Contains("📍 Augustusplatz", embed.Fields[0].Value);
        Assert.Contains("🏷️ Messen", embed.Fields[0].Value);
        Assert.Contains("[Mehr Infos](", embed.Fields[0].Value);
        Assert.StartsWith("3️⃣ So, 11.10. · Straßenmarkt Süd", embed.Fields[2].Name);

        Assert.NotNull(poll);
        Assert.Equal("Wohin gehen wir am Wochenende?", poll!.Question!.Text);
        Assert.False(poll.AllowMultiselect);
        Assert.Equal(PollLayout.Default, poll.LayoutType);
        Assert.Equal(3, poll.Answers!.Count);
        Assert.Equal("Fr 09:00 · Wochenmarkt in der Innenstadt", poll.Answers[0].Text);
        Assert.Equal("Sa 10:00 · Flohmarkt Leuthof", poll.Answers[1].Text);
        Assert.Equal("So 16:00 · Straßenmarkt Süd", poll.Answers[2].Text);
    }

    [Fact]
    public void Build_ComputesPollDuration_UntilEndOfSunday()
    {
        var suggestions = new[] { Event("Wochenmarkt", Friday), Event("Flohmarkt", Sunday) };

        var (_, poll) = WeekendDigestMessageBuilder.Build(suggestions, Friday, PostTime);

        // Thursday 14:15 -> Sunday 23:59 is 81h44m, rounded up to 82 hours.
        Assert.NotNull(poll);
        Assert.Equal(82u, poll!.Duration);
        Assert.InRange(poll.Duration, 1u, 168u);
    }

    [Fact]
    public void Build_PollLayout_IsExplicitDefault_BecauseZeroIsRejectedByDiscord()
    {
        var suggestions = new[] { Event("Wochenmarkt", Friday), Event("Flohmarkt", Sunday) };

        var (_, poll) = WeekendDigestMessageBuilder.Build(suggestions, Friday, PostTime);

        Assert.NotNull(poll);

        // PollLayout has no zero member; the uninitialised default would serialise as layout_type 0
        // and make Discord answer with 50035 "BASE_TYPE_CHOICES: Value must be one of (1,)."
        Assert.Equal(1, (int)poll!.LayoutType);
    }

    [Fact]
    public void Build_SingleSuggestion_OmitsPoll()
    {
        var suggestions = new[] { Event("Wochenmarkt", new DateTime(2026, 10, 10)) };

        var (embed, poll) = WeekendDigestMessageBuilder.Build(suggestions, Friday, PostTime);

        Assert.NotNull(embed);
        Assert.Null(poll);
        Assert.Equal("**1 Tipp** für das Wochenende – stimmt ab, wohin wir gehen! 🗳️", embed.Description);
        Assert.Single(embed.Fields);
    }

    [Fact]
    public void Build_TruncatesPollAnswersToDiscordLimit()
    {
        var longTitle = new string('x', 120);
        var suggestions = new[]
        {
            Event(longTitle, Friday),
            Event("Flohmarkt", Sunday)
        };

        var (_, poll) = WeekendDigestMessageBuilder.Build(suggestions, Friday, PostTime);

        Assert.NotNull(poll);
        Assert.All(poll!.Answers!, a => Assert.True(a.Text!.Length <= 55, $"answer too long: {a.Text.Length}"));
        Assert.EndsWith("…", poll.Answers![0].Text!);
    }

    [Fact]
    public void Build_TruncatesFieldNamesAndValues_ToDiscordLimits()
    {
        var suggestions = new[]
        {
            new WeekendEvent
            {
                Title = new string('t', 400),
                StartDate = Friday,
                EndDate = Friday,
                TimeText = null,
                Location = new string('o', 700),
                Topic = new string('p', 700),
                Url = "https://www.leipzig.de/kultur-und-freizeit/veranstaltungen/eventsingle/event/long"
            },
            Event("Flohmarkt", Sunday)
        };

        var (embed, _) = WeekendDigestMessageBuilder.Build(suggestions, Friday, PostTime);

        var field = embed.Fields[0];
        Assert.True(field.Name.Length <= 256, $"field name too long: {field.Name.Length}");
        Assert.True(field.Value.Length <= 1024, $"field value too long: {field.Value.Length}");
        Assert.True(embed.Title!.Length <= 256);
    }

    [Fact]
    public void Build_FallsBackToAllDayLabel_WhenTimeTextMissing()
    {
        var suggestions = new[]
        {
            Event("Ganztages-Flohmarkt", Friday, timeText: "irrelevant"),
            Event("Konzert", Sunday, timeText: null!)
        };

        var (embed, poll) = WeekendDigestMessageBuilder.Build(suggestions, Friday, PostTime);

        Assert.Contains("⏰ ganztägig", embed.Fields[1].Value);
        Assert.NotNull(poll);
        Assert.Equal("Fr · Ganztages-Flohmarkt", poll!.Answers![0].Text);
        Assert.Equal("So · Konzert", poll.Answers![1].Text);
    }

    [Fact]
    public void Build_FormatsDateRange_WhenWeekendSpansTwoMonths()
    {
        var octoberFriday = new DateTime(2026, 10, 30);
        var suggestions = new[]
        {
            Event("Wochenmarkt", octoberFriday),
            Event("Flohmarkt", new DateTime(2026, 11, 1))
        };

        var (embed, _) = WeekendDigestMessageBuilder.Build(suggestions, octoberFriday, new DateTime(2026, 10, 29, 14, 15, 0));

        Assert.Equal("📅 Wochenend-Tipps — 30.10.–01.11.2026", embed.Title);
    }

    [Fact]
    public void Build_EmptySuggestions_RendersEmptyDescriptionWithoutFields()
    {
        var (embed, poll) = WeekendDigestMessageBuilder.Build(Array.Empty<WeekendEvent>(), Friday, PostTime);

        Assert.Equal("Dieses Wochenende wurden keine passenden Tipps gefunden.", embed.Description);
        Assert.Empty(embed.Fields);
        Assert.Null(poll);
    }
}
