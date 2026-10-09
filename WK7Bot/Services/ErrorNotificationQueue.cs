namespace WK7Bot.Services;

using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using WK7Bot.Models;

/// <summary>
/// Thread-safe bounded queue that buffers error reports for Discord delivery, throttles identical
/// reports within a configurable window and drops the oldest entries once the capacity is reached.
/// </summary>
public class ErrorNotificationQueue
{
    /// <summary>
    /// Default number of buffered reports before the oldest entries are dropped.
    /// </summary>
    public const int DefaultCapacity = 100;

    /// <summary>
    /// Default window in which an identical report is only buffered once.
    /// </summary>
    public static readonly TimeSpan DefaultThrottleWindow = TimeSpan.FromMinutes(5);

    private readonly ConcurrentQueue<ErrorNotification> _queue = new();
    private readonly ConcurrentDictionary<string, DateTimeOffset> _lastReported = new();
    private readonly int _capacity;
    private readonly TimeSpan _throttleWindow;
    private readonly Func<DateTimeOffset> _clock;

    /// <summary>
    /// Initializes a new instance of the <see cref="ErrorNotificationQueue"/> class.
    /// </summary>
    /// <param name="capacity">Maximum number of buffered reports before the oldest entries are dropped.</param>
    /// <param name="throttleWindow">Window in which an identical report is only buffered once.</param>
    /// <param name="clock">Clock source used for throttling; defaults to the current system time.</param>
    public ErrorNotificationQueue(int capacity = DefaultCapacity, TimeSpan? throttleWindow = null, Func<DateTimeOffset>? clock = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);

        _capacity = capacity;
        _throttleWindow = throttleWindow ?? DefaultThrottleWindow;
        _clock = clock ?? (static () => DateTimeOffset.Now);
    }

    /// <summary>
    /// Gets the number of currently buffered reports.
    /// </summary>
    public int Count => _queue.Count;

    /// <summary>
    /// Attempts to buffer a report, dropping it when an identical signature was already buffered within the throttle window.
    /// </summary>
    /// <param name="notification">The error report to buffer.</param>
    /// <returns><see langword="true"/> when the report was buffered; <see langword="false"/> when it was throttled.</returns>
    public bool Enqueue(ErrorNotification notification)
    {
        ArgumentNullException.ThrowIfNull(notification);

        var now = _clock();
        PruneExpiredEntries(now);

        if (_lastReported.TryGetValue(notification.Signature, out var lastReported) && now - lastReported < _throttleWindow)
        {
            return false;
        }

        while (_queue.Count >= _capacity && _queue.TryDequeue(out _))
        {
        }

        _queue.Enqueue(notification);
        _lastReported[notification.Signature] = now;
        return true;
    }

    /// <summary>
    /// Attempts to remove the oldest buffered report from the queue.
    /// </summary>
    /// <param name="notification">The dequeued report, or null when the queue is empty.</param>
    /// <returns><see langword="true"/> when a report was dequeued; otherwise, <see langword="false"/>.</returns>
    public bool TryDequeue([NotNullWhen(true)] out ErrorNotification? notification) => _queue.TryDequeue(out notification);

    /// <summary>
    /// Removes all buffered reports and clears the throttle bookkeeping.
    /// </summary>
    public void Clear()
    {
        while (_queue.TryDequeue(out _))
        {
        }

        _lastReported.Clear();
    }

    /// <summary>
    /// Drops throttle bookkeeping entries whose window has elapsed to keep the signature map small.
    /// </summary>
    /// <param name="now">The current clock reading.</param>
    private void PruneExpiredEntries(DateTimeOffset now)
    {
        foreach (var entry in _lastReported)
        {
            if (now - entry.Value >= _throttleWindow)
            {
                ((ICollection<KeyValuePair<string, DateTimeOffset>>)_lastReported).Remove(entry);
            }
        }
    }
}
