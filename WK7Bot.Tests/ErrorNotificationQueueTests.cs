using WK7Bot.Models;
using WK7Bot.Services;
using Xunit;

namespace WK7Bot.Tests;

public class ErrorNotificationQueueTests
{
    private static ErrorNotification Report(string context = "Cat.A", string message = "boom")
        => new() { Context = context, Message = message };

    [Fact]
    public void Enqueue_FirstReport_ReturnsTrueAndBuffers()
    {
        var queue = new ErrorNotificationQueue();

        Assert.True(queue.Enqueue(Report()));
        Assert.Equal(1, queue.Count);
    }

    [Fact]
    public void Enqueue_IdenticalSignatureWithinWindow_IsThrottled()
    {
        var now = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var queue = new ErrorNotificationQueue(clock: () => now);

        Assert.True(queue.Enqueue(Report()));
        Assert.False(queue.Enqueue(Report()));
        Assert.False(queue.Enqueue(Report()));
        Assert.Equal(1, queue.Count);
    }

    [Fact]
    public void Enqueue_IdenticalSignatureAfterWindow_IsBufferedAgain()
    {
        var now = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var queue = new ErrorNotificationQueue(clock: () => now);

        Assert.True(queue.Enqueue(Report()));

        now = now.Add(ErrorNotificationQueue.DefaultThrottleWindow);

        Assert.True(queue.Enqueue(Report()));
        Assert.Equal(2, queue.Count);
    }

    [Fact]
    public void Enqueue_DifferentSignatures_AreAllBuffered()
    {
        var now = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var queue = new ErrorNotificationQueue(clock: () => now);

        Assert.True(queue.Enqueue(Report(message: "one")));
        Assert.True(queue.Enqueue(Report(message: "two")));
        Assert.True(queue.Enqueue(Report(context: "Cat.B", message: "one")));
        Assert.Equal(3, queue.Count);
    }

    [Fact]
    public void Enqueue_BeyondCapacity_DropsOldest()
    {
        var queue = new ErrorNotificationQueue(capacity: 3);

        Assert.True(queue.Enqueue(Report(message: "first")));
        Assert.True(queue.Enqueue(Report(message: "second")));
        Assert.True(queue.Enqueue(Report(message: "third")));
        Assert.True(queue.Enqueue(Report(message: "fourth")));

        Assert.Equal(3, queue.Count);

        Assert.True(queue.TryDequeue(out var oldest));
        Assert.Equal("second", oldest!.Message);
    }

    [Fact]
    public void TryDequeue_ReturnsFifoOrder()
    {
        var queue = new ErrorNotificationQueue();

        queue.Enqueue(Report(message: "first"));
        queue.Enqueue(Report(message: "second"));

        Assert.True(queue.TryDequeue(out var first));
        Assert.Equal("first", first!.Message);
        Assert.True(queue.TryDequeue(out var second));
        Assert.Equal("second", second!.Message);
        Assert.False(queue.TryDequeue(out _));
    }

    [Fact]
    public void TryDequeue_EmptyQueue_ReturnsFalse()
    {
        var queue = new ErrorNotificationQueue();

        Assert.False(queue.TryDequeue(out var report));
        Assert.Null(report);
    }

    [Fact]
    public void Clear_RemovesBufferedReportsAndThrottleState()
    {
        var queue = new ErrorNotificationQueue();

        Assert.True(queue.Enqueue(Report()));
        queue.Clear();

        Assert.Equal(0, queue.Count);
        Assert.True(queue.Enqueue(Report()));
    }

    [Fact]
    public void Enqueue_Null_Throws()
    {
        var queue = new ErrorNotificationQueue();

        Assert.Throws<ArgumentNullException>(() => queue.Enqueue(null!));
    }

    [Fact]
    public void Constructor_InvalidCapacity_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ErrorNotificationQueue(capacity: 0));
    }

    [Fact]
    public void ErrorNotification_FromException_PopulatesExceptionFields()
    {
        var timestamp = new DateTimeOffset(2026, 2, 3, 4, 5, 6, TimeSpan.Zero);

        var report = ErrorNotification.FromException("AppDomain.UnhandledException", new InvalidOperationException("kaputt"), timestamp);

        Assert.Equal("AppDomain.UnhandledException", report.Context);
        Assert.Equal("kaputt", report.Message);
        Assert.Equal("System.InvalidOperationException", report.ExceptionType);
        Assert.Equal("kaputt", report.ExceptionMessage);
        Assert.Contains("InvalidOperationException: kaputt", report.StackTrace);
        Assert.Equal(timestamp, report.Timestamp);
    }
}
