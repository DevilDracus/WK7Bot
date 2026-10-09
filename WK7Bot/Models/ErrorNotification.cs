namespace WK7Bot.Models;

using System;

/// <summary>
/// Represents a captured error report destined for Discord direct message delivery.
/// </summary>
public class ErrorNotification
{
    /// <summary>
    /// Gets or sets the originating context, typically the logger category or global exception hook name.
    /// </summary>
    public string Context { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the formatted log message text.
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the full CLR type name of the thrown exception, or null when the report carries no exception.
    /// </summary>
    public string? ExceptionType { get; set; }

    /// <summary>
    /// Gets or sets the exception message, or null when the report carries no exception.
    /// </summary>
    public string? ExceptionMessage { get; set; }

    /// <summary>
    /// Gets or sets the formatted exception detail dump (type, message, stack trace and inner exceptions), or null when unavailable.
    /// </summary>
    public string? StackTrace { get; set; }

    /// <summary>
    /// Gets or sets the timestamp at which the error was captured.
    /// </summary>
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.Now;

    /// <summary>
    /// Gets the deduplication signature used to throttle identical error reports within the throttle window.
    /// </summary>
    public string Signature => $"{Context}|{ExceptionType}|{ExceptionMessage}|{Message}";

    /// <summary>
    /// Creates a report from a thrown exception without an accompanying log message.
    /// </summary>
    /// <param name="context">The originating context, typically a global exception hook name.</param>
    /// <param name="exception">The captured exception.</param>
    /// <param name="timestamp">Optional explicit capture timestamp; defaults to the current time.</param>
    /// <returns>A populated error notification report.</returns>
    public static ErrorNotification FromException(string context, Exception exception, DateTimeOffset? timestamp = null)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return new ErrorNotification
        {
            Context = context,
            Message = exception.Message,
            ExceptionType = exception.GetType().FullName,
            ExceptionMessage = exception.Message,
            StackTrace = exception.ToString(),
            Timestamp = timestamp ?? DateTimeOffset.Now,
        };
    }
}
