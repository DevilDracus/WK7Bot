using WK7Bot.Models;
using WK7Bot.Services;
using Xunit;

namespace WK7Bot.Tests;

/// <summary>
/// Covers the pure helpers of the error notification dispatcher that do not need a Discord gateway.
/// </summary>
public class ErrorNotificationDispatcherTests
{
    [Fact]
    public void BuildChannelPing_MentionsEveryConfiguredUser()
    {
        var ping = ErrorNotificationDispatcher.BuildChannelPing(new[] { "123456789012345678", "162201162257399808" });

        Assert.Equal("<@123456789012345678> <@162201162257399808>", ping);
    }

    [Fact]
    public void BuildChannelPing_IsEmpty_WhenNoUsersAreConfigured()
    {
        Assert.Equal(string.Empty, ErrorNotificationDispatcher.BuildChannelPing(Array.Empty<string>()));
        Assert.Equal(string.Empty, ErrorNotificationDispatcher.BuildChannelPing(new List<string>()));
    }

    [Fact]
    public void BuildChannelPing_SkipsMalformedEntriesInsteadOfPingingThem()
    {
        var ping = ErrorNotificationDispatcher.BuildChannelPing(new[] { "not-a-user", "123456789012345678", "+456", "" });

        Assert.Equal("<@123456789012345678>", ping);
    }

    [Fact]
    public void BuildPlainTextMirror_IncludesContextAndExceptionType()
    {
        var notification = new ErrorNotification
        {
            Context = "WK7Bot.Services.DwdWarningBackgroundService",
            ExceptionType = "System.Net.Http.HttpRequestException",
            Message = "An error occurred while evaluating DWD warnings.",
        };

        var result = ErrorNotificationDispatcher.BuildPlainTextMirror(notification);

        Assert.Contains("⚠️ DwdWarningBackgroundService", result);
        Assert.Contains("System.Net.Http.HttpRequestException", result);
    }

    [Fact]
    public void BuildPlainTextMirror_IncludesStackTraceHash()
    {
        var notification = new ErrorNotification
        {
            Context = "WK7Bot.Services.SomeService",
            ExceptionType = "System.Exception",
            Message = "Test",
            StackTrace = "at SomeService.DoWork()\r\nat OtherService.Run()",
        };

        var result = ErrorNotificationDispatcher.BuildPlainTextMirror(notification);

        // Hash is an 8-character hex string in brackets after the exception type.
        Assert.Matches(@"\[\b[0-9A-F]{8}\b\]", result);
    }

    [Fact]
    public void BuildPlainTextMirror_HashIsDeterministic()
    {
        var notification = new ErrorNotification
        {
            Context = "WK7Bot.Services.StableService",
            ExceptionType = "System.Exception",
            Message = "Test",
            StackTrace = "at StableService.DoWork()\r\nat OtherService.Run()",
        };

        var first = ErrorNotificationDispatcher.BuildPlainTextMirror(notification);
        var second = ErrorNotificationDispatcher.BuildPlainTextMirror(notification);

        Assert.Equal(first, second);
    }

    [Fact]
    public void BuildPlainTextMirror_IncludesMessageExcerpt()
    {
        var notification = new ErrorNotification
        {
            Context = "WK7Bot.Services.GeminiFoodService",
            ExceptionType = "System.TimeoutException",
            Message = "The request was canceled due to the configured HttpClient.Timeout of 30 seconds elapsing.",
        };

        var result = ErrorNotificationDispatcher.BuildPlainTextMirror(notification);

        Assert.Contains("—", result);
        Assert.Contains("The request was canceled", result);
    }

    [Fact]
    public void BuildPlainTextMirror_TruncatesLongMessages()
    {
        var notification = new ErrorNotification
        {
            Context = "WK7Bot.Services.SteamService",
            ExceptionType = "System.InvalidOperationException",
            Message = new string('A', 200),
        };

        var result = ErrorNotificationDispatcher.BuildPlainTextMirror(notification);

        Assert.True(result.Length <= 200, $"Expected length <= 200, got {result.Length}");
        Assert.EndsWith("...", result);
    }

    [Fact]
    public void BuildPlainTextMirror_HandlesMissingExceptionType()
    {
        var notification = new ErrorNotification
        {
            Context = "WK7Bot.Services.FoodPublisherService",
            ExceptionType = null,
            Message = "Something went wrong",
        };

        var result = ErrorNotificationDispatcher.BuildPlainTextMirror(notification);

        Assert.Contains("⚠️ FoodPublisherService: Error", result);
    }

    [Fact]
    public void BuildPlainTextMirror_HandlesEmptyMessage()
    {
        var notification = new ErrorNotification
        {
            Context = "AppDomain.UnhandledException",
            ExceptionType = "System.Exception",
            Message = string.Empty,
        };

        var result = ErrorNotificationDispatcher.BuildPlainTextMirror(notification);

        // Hash is always included now; assert the stable prefix.
        Assert.StartsWith("⚠️ UnhandledException: System.Exception", result);
    }

    [Fact]
    public void BuildPlainTextMirror_HandlesShortContext()
    {
        var notification = new ErrorNotification
        {
            Context = "SimpleContext",
            ExceptionType = "System.ArgumentException",
            Message = "Bad argument",
        };

        var result = ErrorNotificationDispatcher.BuildPlainTextMirror(notification);

        Assert.Contains("⚠️ SimpleContext", result);
    }
}
