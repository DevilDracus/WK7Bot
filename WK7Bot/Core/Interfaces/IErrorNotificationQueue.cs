namespace WK7Bot.Core.Interfaces;

using WK7Bot.Models;

/// <summary>
/// Contract for the thread-safe bounded error notification queue.
/// </summary>
public interface IErrorNotificationQueue
{
    /// <summary>Gets the number of currently buffered reports.</summary>
    int Count { get; }

    /// <summary>Attempts to buffer a report.</summary>
    bool Enqueue(ErrorNotification notification);

    /// <summary>Attempts to remove the oldest buffered report.</summary>
    bool TryDequeue(out ErrorNotification? notification);

    /// <summary>Removes all buffered reports.</summary>
    void Clear();
}