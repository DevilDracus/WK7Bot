using System.Net;
using System.Net.Http;

namespace WK7Bot.Services;

/// <summary>
/// Abstraction over the shared HTTP transport layer used by game API clients, covering concurrency throttling,
/// transient-failure retries, and transient-status classification.
/// </summary>
public interface IGameApiHttpTransport
{
    /// <summary>
    /// Sends a request while honouring the API concurrency throttle.
    /// </summary>
    /// <param name="sendFactory">Factory creating the HTTP request; invoked per attempt.</param>
    /// <param name="cancellationToken">A cancellation token to monitor for cancellation requests.</param>
    /// <returns>The received <see cref="HttpResponseMessage"/>.</returns>
    Task<HttpResponseMessage> SendThrottledAsync(Func<Task<HttpResponseMessage>> sendFactory, CancellationToken cancellationToken);

    /// <summary>
    /// Executes a request with linear-backoff retries on transient (429 / 5xx) failures.
    /// </summary>
    /// <param name="sendFactory">Factory creating the HTTP request; invoked per attempt.</param>
    /// <param name="operationName">The logical operation name used in log messages.</param>
    /// <param name="cancellationToken">A cancellation token to monitor for cancellation requests.</param>
    /// <returns>The final <see cref="HttpResponseMessage"/>; the caller owns and must dispose it.</returns>
    Task<HttpResponseMessage> ExecuteWithRetryAsync(Func<Task<HttpResponseMessage>> sendFactory, string operationName, CancellationToken cancellationToken);

    /// <summary>
    /// Determines whether a response represents a transient failure eligible for retry (429 or 5xx).
    /// </summary>
    /// <param name="response">The response to evaluate.</param>
    /// <returns>True when the request should be retried; otherwise false.</returns>
    bool IsTransientFailure(HttpResponseMessage response);
}
