namespace WK7Bot.Core.Utilities;

using System.Net;
using System.Net.Http;

/// <summary>
/// Shared HTTP retry helper that collapses the three hand-rolled retry loops in the codebase
/// (BaseGameApiClient, WishlistDealService, GeminiFoodService) into one configurable implementation.
/// Each caller supplies its own transient-failure predicate, delay calculator, and attempt count,
/// so service-specific behaviour (linear vs. exponential backoff, 403 handling, 503 extra delay)
/// is preserved while the loop mechanics live in one place.
/// </summary>
public static class HttpRetry
{
    /// <summary>
    /// Executes <paramref name="sendFactory" /> with a sensible default retry policy: retries on HTTP 5xx,
    /// 429 Too Many Requests, and transient exceptions (HttpRequestException, TimeoutException,
    /// TaskCanceledException, IOException) with exponential backoff (2s, 4s, 8s, ...).
    /// </summary>
    /// <param name="sendFactory">Factory that produces an HTTP response (may be called multiple times).</param>
    /// <param name="maxAttempts">Maximum number of attempts before giving up. Defaults to 3.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The final HTTP response (caller owns disposal).</returns>
    public static Task<HttpResponseMessage> ExecuteAsync(
        Func<Task<HttpResponseMessage>> sendFactory,
        int maxAttempts = 3,
        CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(
            sendFactory,
            maxAttempts,
            isTransientResponse: static r =>
                (int)r.StatusCode >= 500 || r.StatusCode == System.Net.HttpStatusCode.TooManyRequests,
            delayForAttempt: static attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt)),
            isTransientException: static ex =>
                ex is HttpRequestException or TimeoutException or TaskCanceledException or IOException,
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Executes <paramref name="sendFactory" /> with retry on transient failures.
    /// </summary>
    /// <param name="sendFactory">Factory that produces an HTTP response (may be called multiple times).</param>
    /// <param name="maxAttempts">Maximum number of attempts before giving up.</param>
    /// <param name="isTransient">Predicate deciding whether a response is a transient failure worth retrying.</param>
    /// <param name="delayForAttempt">Delay before the next attempt given the current attempt number (1-based).</param>
    /// <param name="extraDelay">Optional extra delay applied after the normal delay (e.g. for 503).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The final HTTP response (caller owns disposal).</returns>
    public static async Task<HttpResponseMessage> ExecuteAsync(
        Func<Task<HttpResponseMessage>> sendFactory,
        int maxAttempts,
        Func<HttpResponseMessage, bool> isTransientResponse,
        Func<int, TimeSpan> delayForAttempt,
        Func<HttpResponseMessage, TimeSpan>? extraDelay = null,
        Func<Exception, bool>? isTransientException = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sendFactory);
        ArgumentNullException.ThrowIfNull(isTransientResponse);
        ArgumentNullException.ThrowIfNull(delayForAttempt);

        if (maxAttempts < 1)
        {
            maxAttempts = 1;
        }

        HttpResponseMessage? response = null;
        Exception? lastException = null;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                response = await sendFactory().ConfigureAwait(false);
                lastException = null;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (isTransientException is not null && isTransientException(ex) && attempt < maxAttempts)
            {
                lastException = ex;
                var delay = delayForAttempt(attempt);
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                continue;
            }

            if (response is not null && isTransientResponse(response) && attempt < maxAttempts)
            {
                TimeSpan extra = TimeSpan.Zero;
                if (extraDelay is not null)
                {
                    extra = extraDelay(response);
                }

                response.Dispose();

                var delay = delayForAttempt(attempt);
                if (extra > TimeSpan.Zero)
                {
                    delay += extra;
                }

                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                response = null;
                continue;
            }

            // Success or non-transient or last attempt.
            return response ?? throw lastException ?? new InvalidOperationException("No response received.");
        }

        return response ?? throw lastException ?? new InvalidOperationException("No response received.");
    }
}
