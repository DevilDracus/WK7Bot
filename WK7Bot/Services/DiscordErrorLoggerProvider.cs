namespace WK7Bot.Services;

using Microsoft.Extensions.Logging;
using WK7Bot.Models;

/// <summary>
/// Logging provider that captures Error and Critical log events into an <see cref="ErrorNotificationQueue"/>
/// for Discord direct message delivery, in addition to the regular Home Assistant log output.
/// </summary>
public sealed class DiscordErrorLoggerProvider : ILoggerProvider
{
    private readonly ErrorNotificationQueue _queue;

    /// <summary>
    /// Initializes a new instance of the <see cref="DiscordErrorLoggerProvider"/> class.
    /// </summary>
    /// <param name="queue">The queue that buffers captured reports for Discord delivery.</param>
    public DiscordErrorLoggerProvider(ErrorNotificationQueue queue)
    {
        _queue = queue ?? throw new ArgumentNullException(nameof(queue));
    }

    /// <summary>
    /// Creates a logger that forwards Error and Critical events into the notification queue.
    /// </summary>
    /// <param name="categoryName">The logger category name, used as the report context.</param>
    /// <returns>The created logger.</returns>
    public ILogger CreateLogger(string categoryName) => new QueueingErrorLogger(categoryName, _queue);

    /// <summary>
    /// Releases the provider resources; the shared queue outlives the provider.
    /// </summary>
    public void Dispose()
    {
    }

    /// <summary>
    /// Logger that buffers Error and Critical events into the shared notification queue.
    /// </summary>
    private sealed class QueueingErrorLogger : ILogger
    {
        private readonly string _categoryName;
        private readonly ErrorNotificationQueue _queue;

        public QueueingErrorLogger(string categoryName, ErrorNotificationQueue queue)
        {
            _categoryName = categoryName;
            _queue = queue;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Error;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel < LogLevel.Error)
            {
                return;
            }

            string message;
            try
            {
                message = formatter(state, exception);
            }
            catch (Exception)
            {
                message = state?.ToString() ?? string.Empty;
            }

            // Enqueue must never throw: this provider runs inside the logging pipeline, so an
            // exception here would escape ILogger.Log and crash whatever code was reporting the
            // error — turning a reportable failure into a process-wide one.
            try
            {
                _queue.Enqueue(new ErrorNotification
                {
                    Context = _categoryName,
                    Message = message,
                    ExceptionType = exception?.GetType().FullName,
                    ExceptionMessage = exception?.Message,
                    StackTrace = exception?.ToString(),
                    Timestamp = DateTimeOffset.Now,
                });
            }
            catch (Exception)
            {
                // Notifications are best-effort; losing one is strictly better than crashing the
                // caller. Nothing can be logged from here — that would re-enter this provider.
            }
        }
    }
}
