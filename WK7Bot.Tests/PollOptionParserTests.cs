using WK7Bot.Core.Utilities;
using Xunit;

namespace WK7Bot.Tests;

public class PollOptionParserTests
{
    [Fact]
    public void Parse_SplitsOnPipeAndNewlines()
    {
        var answers = PollOptionParser.Parse("Ja | Nein\nVielleicht", out var error);

        Assert.Null(error);
        Assert.Equal(new[] { "Ja", "Nein", "Vielleicht" }, answers);
    }

    [Fact]
    public void Parse_TrimsAndDropsEmptyEntries()
    {
        var answers = PollOptionParser.Parse("  A  ||  B  |   ", out var error);

        Assert.Null(error);
        Assert.Equal(new[] { "A", "B" }, answers);
    }

    [Fact]
    public void Parse_TruncatesLongAnswers()
    {
        var longAnswer = new string('x', 80);
        var answers = PollOptionParser.Parse($"A | {longAnswer}", out var error);

        Assert.Null(error);
        Assert.Equal(PollOptionParser.MaxOptionLength, answers[1].Length);
    }

    [Fact]
    public void Parse_RequiresTwoAnswers()
    {
        var answers = PollOptionParser.Parse("Nur eine", out var error);

        Assert.Empty(answers);
        Assert.NotNull(error);
    }

    [Fact]
    public void Parse_RejectsMoreThanTenAnswers()
    {
        var input = string.Join(" | ", Enumerable.Range(1, 11).Select(i => $"Option {i}"));

        var answers = PollOptionParser.Parse(input, out var error);

        Assert.Empty(answers);
        Assert.NotNull(error);
    }

    [Fact]
    public void Parse_EmptyInput_ReturnsError()
    {
        var answers = PollOptionParser.Parse("   ", out var error);

        Assert.Empty(answers);
        Assert.NotNull(error);
    }

    [Fact]
    public void ParseQuestion_TrimsAndValidates()
    {
        var question = PollOptionParser.ParseQuestion("  Wohin?  ", out var error);
        Assert.Null(error);
        Assert.Equal("Wohin?", question);

        PollOptionParser.ParseQuestion("   ", out var emptyError);
        Assert.NotNull(emptyError);
    }
}
