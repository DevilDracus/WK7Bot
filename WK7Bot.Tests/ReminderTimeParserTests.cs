using WK7Bot.Core.Utilities;
using Xunit;

namespace WK7Bot.Tests;

public class ReminderTimeParserTests
{
    private static readonly DateTime Now = new(2026, 10, 17, 14, 0, 0);

    [Theory]
    [InlineData("in 30m", 30)]
    [InlineData("in 2h", 120)]
    [InlineData("in 1d", 1440)]
    [InlineData("45m", 45)]
    [InlineData("in 90s", 1.5)]
    [InlineData("in 1 woche", 10080)]
    public void TryParse_RelativeOffsets(string input, double expectedMinutes)
    {
        var ok = ReminderTimeParser.TryParse(input, Now, out var dueAt, out var error);

        Assert.True(ok, error);
        Assert.Equal(Now.AddMinutes(expectedMinutes), dueAt, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void TryParse_ClockTimeLaterToday()
    {
        var ok = ReminderTimeParser.TryParse("18:30", Now, out var dueAt, out var error);

        Assert.True(ok, error);
        Assert.Equal(new DateTime(2026, 10, 17, 18, 30, 0), dueAt);
    }

    [Fact]
    public void TryParse_ClockTimeAlreadyPassed_RollsToTomorrow()
    {
        var ok = ReminderTimeParser.TryParse("09:00", Now, out var dueAt, out var error);

        Assert.True(ok, error);
        Assert.Equal(new DateTime(2026, 10, 18, 9, 0, 0), dueAt);
    }

    [Theory]
    [InlineData("morgen 08:00", 18, 8)]
    [InlineData("übermorgen 20:15", 19, 20)]
    [InlineData("uebermorgen 06:05", 19, 6)]
    public void TryParse_DayWithClockTime(string input, int expectedDay, int expectedHour)
    {
        var ok = ReminderTimeParser.TryParse(input, Now, out var dueAt, out var error);

        Assert.True(ok, error);
        Assert.Equal(new DateTime(2026, 10, expectedDay, expectedHour, dueAt.Minute, 0), dueAt);
    }

    [Theory]
    [InlineData("morgen", 18)]
    [InlineData("übermorgen", 19)]
    public void TryParse_BareDay_DefaultsToEightAm(string input, int expectedDay)
    {
        var ok = ReminderTimeParser.TryParse(input, Now, out var dueAt, out var error);

        Assert.True(ok, error);
        Assert.Equal(new DateTime(2026, 10, expectedDay, 8, 0, 0), dueAt);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("bald")]
    [InlineData("25:00")]
    [InlineData("12:75")]
    [InlineData("in 0m")]
    public void TryParse_InvalidInput_ReturnsError(string input)
    {
        var ok = ReminderTimeParser.TryParse(input, Now, out _, out var error);

        Assert.False(ok);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void TryParse_TooFarInTheFuture_ReturnsError()
    {
        var ok = ReminderTimeParser.TryParse("in 400d", Now, out _, out var error);

        Assert.False(ok);
        Assert.Contains("Zukunft", error, StringComparison.OrdinalIgnoreCase);
    }
}
