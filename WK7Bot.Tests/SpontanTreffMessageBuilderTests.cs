using Discord;
using WK7Bot.Core.Entities;
using WK7Bot.Core.Utilities;
using Xunit;

namespace WK7Bot.Tests;

public class SpontanTreffMessageBuilderTests
{
    private static readonly DateTime Now = new(2026, 10, 6, 18, 12, 0);

    private static SpontanTreff Meetup(DateTime? expiresAt = null, bool closed = false) => new()
    {
        Id = 7,
        GuildId = 1,
        ChannelId = 2,
        MessageId = 3,
        OrganizerId = 42,
        OrganizerName = "Tester",
        Plan = "Kaffee im Innenhof?",
        Location = "Innenhof",
        CreatedAt = Now.AddMinutes(-3),
        ExpiresAt = expiresAt ?? Now.AddMinutes(27),
        Closed = closed
    };

    private static List<ButtonComponent> Buttons(MessageComponent component)
        => component.Components
            .SelectMany(row => row.Components)
            .OfType<ButtonComponent>()
            .ToList();

    private static string FieldValue(Embed embed, string name)
        => embed.Fields.Single(f => f.Name == name).Value;

    [Fact]
    public void Build_ActiveMeetup_RendersPlanLocationAndButtons()
    {
        var (embed, components) = SpontanTreffMessageBuilder.Build(Meetup(), Array.Empty<SpontanTreffResponse>(), Now);

        Assert.Equal("⚡ Spontan-Treff", embed.Title);
        Assert.Contains("Kaffee im Innenhof?", embed.Description);
        Assert.Contains("📍 Innenhof", embed.Description);
        Assert.Contains("<@42>", embed.Description);
        Assert.Contains("Noch 27 Min.", embed.Description);
        Assert.Contains("Offen bis 18:39 Uhr", embed.Footer!.Value.Text);

        var buttons = Buttons(components);
        Assert.Equal(2, buttons.Count);
        Assert.Equal("✅ Auf dem Weg", buttons[0].Label);
        Assert.Equal("spontan-treff:go:7", buttons[0].CustomId);
        Assert.Equal(ButtonStyle.Success, buttons[0].Style);
        Assert.Equal("🚫 Absagen", buttons[1].Label);
        Assert.Equal("spontan-treff:pass:7", buttons[1].CustomId);
    }

    [Fact]
    public void Build_ShowsAnswersGroupedByState()
    {
        var responses = new List<SpontanTreffResponse>
        {
            new() { MeetupId = 7, UserId = 11, Going = true, RespondedAt = Now },
            new() { MeetupId = 7, UserId = 12, Going = true, RespondedAt = Now.AddMinutes(1) },
            new() { MeetupId = 7, UserId = 13, Going = false, RespondedAt = Now.AddMinutes(2) }
        };

        var (embed, _) = SpontanTreffMessageBuilder.Build(Meetup(), responses, Now);

        Assert.Contains("<@11>", FieldValue(embed, "✅ Auf dem Weg (2)"));
        Assert.Contains("<@12>", FieldValue(embed, "✅ Auf dem Weg (2)"));
        Assert.DoesNotContain("<@13>", FieldValue(embed, "✅ Auf dem Weg (2)"));
        Assert.Equal("<@13>", FieldValue(embed, "🚫 Abgesagt (1)"));
    }

    [Fact]
    public void Build_WithoutAnswers_UsesPlaceholder()
    {
        var (embed, _) = SpontanTreffMessageBuilder.Build(Meetup(), Array.Empty<SpontanTreffResponse>(), Now);

        Assert.Equal("—", FieldValue(embed, "✅ Auf dem Weg (0)"));
        Assert.Equal("—", FieldValue(embed, "🚫 Abgesagt (0)"));
    }

    [Fact]
    public void Build_LongAnswerList_StaysWithinFieldLimit()
    {
        var responses = Enumerable.Range(1, 200)
            .Select(i => new SpontanTreffResponse { MeetupId = 7, UserId = (ulong)i, Going = true, RespondedAt = Now })
            .ToList();

        var (embed, _) = SpontanTreffMessageBuilder.Build(Meetup(), responses, Now);

        var value = FieldValue(embed, "✅ Auf dem Weg (200)");
        Assert.True(value.Length <= 1024, $"Field value is {value.Length} characters.");
        Assert.Contains("weitere", value);
    }

    [Fact]
    public void Build_ExpiredMeetup_RemovesButtonsAndMarksClosed()
    {
        var (embed, components) = SpontanTreffMessageBuilder.Build(
            Meetup(Now.AddMinutes(-1)), Array.Empty<SpontanTreffResponse>(), Now);

        Assert.Equal("⚡ Spontan-Treff · vorbei", embed.Title);
        Assert.Contains("abgelaufen", embed.Description);
        Assert.Contains("Beendet", embed.Footer!.Value.Text);
        Assert.Empty(Buttons(components));
    }

    [Fact]
    public void Build_ClosedMeetup_RemovesButtonsBeforeExpiry()
    {
        var (embed, components) = SpontanTreffMessageBuilder.Build(
            Meetup(closed: true), Array.Empty<SpontanTreffResponse>(), Now);

        Assert.Equal("⚡ Spontan-Treff · vorbei", embed.Title);
        Assert.Empty(Buttons(components));
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void IsActive_FollowsClosedFlag(bool closed, bool expected)
    {
        Assert.Equal(expected, SpontanTreffMessageBuilder.IsActive(Meetup(closed: closed), Now));
    }

    [Fact]
    public void IsActive_FalseOnceDeadlinePassed()
    {
        var meetup = Meetup(Now.AddSeconds(-1));

        Assert.False(SpontanTreffMessageBuilder.IsActive(meetup, Now));
    }

    [Fact]
    public void BuildCustomId_CombinesPrefixActionAndId()
    {
        Assert.Equal("spontan-treff:go:7", SpontanTreffMessageBuilder.BuildCustomId(SpontanTreffMessageBuilder.GoAction, 7));
        Assert.Equal("spontan-treff:pass:7", SpontanTreffMessageBuilder.BuildCustomId(SpontanTreffMessageBuilder.PassAction, 7));
    }
}
