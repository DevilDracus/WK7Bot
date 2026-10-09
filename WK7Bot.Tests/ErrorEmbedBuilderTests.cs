using WK7Bot.Core.Utilities;
using WK7Bot.Models;
using Xunit;

namespace WK7Bot.Tests;

public class ErrorEmbedBuilderTests
{
    private static ErrorNotification FullReport() => new()
    {
        Context = "WK7Bot.Services.RssPollingBackgroundService",
        Message = "Feed gaming failed",
        ExceptionType = "System.InvalidOperationException",
        ExceptionMessage = "kaputt",
        StackTrace = "System.InvalidOperationException: kaputt\n   at RssPolling.Execute()",
        Timestamp = new DateTimeOffset(2026, 3, 14, 15, 9, 26, TimeSpan.Zero),
    };

    [Fact]
    public void Build_FullReport_RendersAllSections()
    {
        var notification = FullReport();

        var embed = ErrorEmbedBuilder.Build(notification);

        Assert.Equal("⚠️ Fehler in RssPollingBackgroundService", embed.Title);
        Assert.Equal(ErrorEmbedBuilder.ErrorColor, embed.Color);
        Assert.Equal(ErrorEmbedBuilder.FooterText, embed.Footer?.Text);

        Assert.Contains(embed.Fields, f => f.Name == "Ausnahme" && f.Value == "System.InvalidOperationException: kaputt");
        Assert.Contains(embed.Fields, f => f.Name == "Meldung" && f.Value == "```\nFeed gaming failed\n```");
        Assert.Contains(embed.Fields, f => f.Name == "Stack-Trace" && f.Value.Contains("at RssPolling.Execute()"));
        Assert.Contains(embed.Fields, f => f.Name == "Kontext" && f.Value == "WK7Bot.Services.RssPollingBackgroundService");
        Assert.Contains(embed.Fields, f => f.Name == "Zeitpunkt" && f.Value == notification.Timestamp.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss zzz"));
        Assert.Equal(5, embed.Fields.Length);
    }

    [Fact]
    public void Build_LogOnlyReport_OmitsExceptionSections()
    {
        var embed = ErrorEmbedBuilder.Build(new ErrorNotification { Context = "Cat.A", Message = "only a message" });

        Assert.DoesNotContain(embed.Fields, f => f.Name == "Ausnahme");
        Assert.DoesNotContain(embed.Fields, f => f.Name == "Stack-Trace");
        Assert.Contains(embed.Fields, f => f.Name == "Meldung");
        Assert.Equal(3, embed.Fields.Length);
    }

    [Fact]
    public void Build_DuplicatedExceptionMessage_OmitsMeldung()
    {
        var notification = ErrorNotification.FromException("Cat.A", new InvalidOperationException("same text"), new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

        var embed = ErrorEmbedBuilder.Build(notification);

        Assert.Contains(embed.Fields, f => f.Name == "Ausnahme" && f.Value == "System.InvalidOperationException: same text");
        Assert.DoesNotContain(embed.Fields, f => f.Name == "Meldung");
        Assert.Contains(embed.Fields, f => f.Name == "Stack-Trace");
    }

    [Fact]
    public void Build_OversizedMessage_IsTruncatedWithinFieldLimit()
    {
        var embed = ErrorEmbedBuilder.Build(new ErrorNotification { Context = "Cat.A", Message = new string('x', 5000) });

        var meldung = embed.Fields.Single(f => f.Name == "Meldung").Value;

        Assert.True(meldung.Length <= 1024);
        Assert.EndsWith("…\n```", meldung);
    }

    [Fact]
    public void Build_OversizedContextTitle_IsTruncated()
    {
        var embed = ErrorEmbedBuilder.Build(new ErrorNotification { Context = new string('a', 400), Message = "m" });

        Assert.NotNull(embed.Title);
        Assert.True(embed.Title!.Length <= 256);
    }

    [Fact]
    public void Build_EmptyContext_FallsBackToUnknown()
    {
        var embed = ErrorEmbedBuilder.Build(new ErrorNotification { Context = string.Empty, Message = "m" });

        Assert.Equal("⚠️ Fehler in Unbekannt", embed.Title);
    }

    [Fact]
    public void Build_EmbeddedCodeFences_AreNeutralized()
    {
        var embed = ErrorEmbedBuilder.Build(new ErrorNotification { Context = "Cat.A", Message = "start ``` bad end" });

        var meldung = embed.Fields.Single(f => f.Name == "Meldung").Value;

        Assert.Contains("start ''' bad end", meldung);
        Assert.Equal(2, meldung.Split("```").Length - 1);
    }

    [Fact]
    public void Build_NullNotification_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => ErrorEmbedBuilder.Build(null!));
    }
}
