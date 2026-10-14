using System.Net;
using System.Net.Http;
using Microsoft.Extensions.Logging;
using WK7Bot.Options;

namespace WK7Bot.Services;

public partial class SteamService
{
    /// <summary>
    /// Executes a Steam Web API GET request while limiting achievement endpoint concurrency and retrying transient failures (429/5xx) with linear backoff.
    /// </summary>
    /// <param name="url">The fully qualified request URL.</param>
    /// <param name="operationName">The logical Steam operation name used in log messages.</param>
    /// <param name="cancellationToken">A cancellation token to monitor for cancellation requests.</param>
    /// <returns>The final <see cref="HttpResponseMessage"/>; the caller owns and must dispose it.</returns>
    private async Task<HttpResponseMessage> GetSteamApiWithRetryAsync(string url, string operationName, CancellationToken cancellationToken)
    {
        HttpResponseMessage response = await SendThrottledAsync(url, cancellationToken);

        for (int attempt = 1; attempt < MaxAttempts && IsTransientFailure(response); attempt++)
        {
            response.Dispose();

            var delay = TimeSpan.FromMilliseconds(RetryDelay.TotalMilliseconds * attempt);
            _logger.LogWarning(
                "Steam API {Operation} returned a transient failure (attempt {Attempt}/{MaxAttempts}); retrying after {Delay}.",
                operationName,
                attempt,
                MaxAttempts,
                delay);

            await Task.Delay(delay, cancellationToken);
            response = await SendThrottledAsync(url, cancellationToken);
        }

        if (IsTransientFailure(response))
        {
            _logger.LogWarning(
                "Steam API {Operation} still failing with status code {StatusCode} after {MaxAttempts} attempts.",
                operationName,
                response.StatusCode,
                MaxAttempts);
        }

        return response;
    }

    /// <summary>
    /// Performs a GET request under the shared achievement API concurrency throttle.
    /// </summary>
    /// <param name="url">The fully qualified request URL.</param>
    /// <param name="cancellationToken">A cancellation token to monitor for cancellation requests.</param>
    /// <returns>The received <see cref="HttpResponseMessage"/>.</returns>
    private async Task<HttpResponseMessage> SendThrottledAsync(string url, CancellationToken cancellationToken)
    {
        await _achievementApiThrottle.WaitAsync(cancellationToken);
        try
        {
            return await _httpClient.GetAsync(url, cancellationToken);
        }
        finally
        {
            _achievementApiThrottle.Release();
        }
    }

    /// <summary>
    /// Determines whether a response represents a transient Steam API failure eligible for retry (429 or 5xx).
    /// </summary>
    /// <param name="response">The response to evaluate.</param>
    /// <returns>True when the request should be retried; otherwise false.</returns>
    private static bool IsTransientFailure(HttpResponseMessage response)
    {
        return response.StatusCode == HttpStatusCode.TooManyRequests
            || (int)response.StatusCode >= 500;
    }
}
