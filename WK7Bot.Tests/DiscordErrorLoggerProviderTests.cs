using Microsoft.Extensions.Logging;
using WK7Bot.Services;
using Xunit;

namespace WK7Bot.Tests;

public class DiscordErrorLoggerProviderTests
{
    private static ILogger CreateLogger(ErrorNotificationQueue queue, string category = "WK7Bot.Services.RssPollingBackgroundService")
        => new DiscordErrorLoggerProvider(queue).CreateLogger(category);

    [Fact]
    public void LogError_WithException_EnqueuesFullReport()
    {
        var queue = new ErrorNotificationQueue();
        var logger = CreateLogger(queue);

        logger.LogError(new InvalidOperationException("kaputt"), "Feed {Name} failed", "gaming");

        Assert.True(queue.TryDequeue(out var report));
        Assert.Equal("WK7Bot.Services.RssPollingBackgroundService", report!.Context);
        Assert.Equal("Feed gaming failed", report.Message);
        Assert.Equal("System.InvalidOperationException", report.ExceptionType);
        Assert.Equal("kaputt", report.ExceptionMessage);
        Assert.Contains("InvalidOperationException: kaputt", report.StackTrace);
        Assert.False(queue.TryDequeue(out _));
    }

    [Fact]
    public void LogCritical_WithoutException_EnqueuesMessageOnly()
    {
        var queue = new ErrorNotificationQueue();
        var logger = CreateLogger(queue);

        logger.LogCritical("No valid Discord bot token was provided.");

        Assert.True(queue.TryDequeue(out var report));
        Assert.Equal("No valid Discord bot token was provided.", report!.Message);
        Assert.Null(report.ExceptionType);
        Assert.Null(report.ExceptionMessage);
        Assert.Null(report.StackTrace);
    }

    [Fact]
    public void LevelsBelowError_AreNotEnabledAndNotCaptured()
    {
        var queue = new ErrorNotificationQueue();
        var logger = CreateLogger(queue);

        Assert.False(logger.IsEnabled(LogLevel.Trace));
        Assert.False(logger.IsEnabled(LogLevel.Debug));
        Assert.False(logger.IsEnabled(LogLevel.Information));
        Assert.False(logger.IsEnabled(LogLevel.Warning));
        Assert.True(logger.IsEnabled(LogLevel.Error));
        Assert.True(logger.IsEnabled(LogLevel.Critical));

        logger.LogDebug("debug details");
        logger.LogInformation("informational");
        logger.LogWarning("careful");

        Assert.Equal(0, queue.Count);
    }

    [Fact]
    public void Log_FailingFormatter_FallsBackToStateText()
    {
        var queue = new ErrorNotificationQueue();
        var logger = CreateLogger(queue);

        logger.Log(LogLevel.Error, new EventId(0), "raw state text", null, (_, _) => throw new InvalidOperationException("formatter bug"));

        Assert.True(queue.TryDequeue(out var report));
        Assert.Equal("raw state text", report!.Message);
    }

    [Fact]
    public void BeginScope_ReturnsNull()
    {
        var queue = new ErrorNotificationQueue();
        var logger = CreateLogger(queue);

        Assert.Null(logger.BeginScope("scope state"));
    }

    [Fact]
    public void Dispose_KeepsQueueUsable()
    {
        var queue = new ErrorNotificationQueue();
        var provider = new DiscordErrorLoggerProvider(queue);

        provider.Dispose();

        Assert.True(queue.Enqueue(new WK7Bot.Models.ErrorNotification { Context = "Cat", Message = "still works" }));
    }

    [Fact]
    public void Constructor_NullQueue_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new DiscordErrorLoggerProvider(null!));
    }
}
