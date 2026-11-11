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
    /// <summary>Default number of retry attempts for the simplified overload.</summary>
    public const int DefaultMaxAttempts = 3;

    /// <summary>Minimum sensible attempt count; anything lower is normalized to this.</summary>
    private const int MinAttempts = 1;

    /// <summary>Base multiplier for the default linear backoff (milliseconds per attempt).</summary>
    private const int LinearBackoffBaseMilliseconds = 500;

    /// <summary>Base raised to the attempt number for the default exponential backoff.</summary>
    private const double ExponentialBackoffBase = 2;

    /// <summary>
    /// Default transient response predicate: retries on HTTP 5xx and 429 Too Many Requests.
    /// </summary>
    public static bool IsTransientResponse(HttpResponseMessage response) =>
        (int)response.StatusCode >= 500 || response.StatusCode == HttpStatusCode.TooManyRequests;

    /// <summary>
    /// Default transient exception predicate: retries on HttpRequestException, TimeoutException,
    /// TaskCanceledException, and IOException.
    /// </summary>
    public static bool IsTransientException(Exception ex) =>
        ex is HttpRequestException or TimeoutException or TaskCanceledException or IOException;

    /// <summary>
    /// Default exponential backoff: 2s, 4s, 8s, ...
    /// </summary>
    public static TimeSpan ExponentialBackoff(int attempt) =>
        TimeSpan.FromSeconds(Math.Pow(ExponentialBackoffBase, attempt));

    /// <summary>
    /// Default linear backoff: 500ms, 1s, 1.5s, ...
    /// </summary>
    public static TimeSpan LinearBackoff(int attempt) =>
        TimeSpan.FromMilliseconds(LinearBackoffBaseMilliseconds * attempt);

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
        int maxAttempts = DefaultMaxAttempts,
        CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(
            sendFactory,
            maxAttempts,
            isTransientResponse: IsTransientResponse,
            delayForAttempt: ExponentialBackoff,
            isTransientException: IsTransientException,
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Executes <paramref name="sendFactory" /> with retry on transient failures.
    /// </summary>
    /// <param name="sendFactory">Factory that produces an HTTP response (may be called multiple times).</param>
    /// <param name="maxAttempts">Maximum number of attempts before giving up.</param>
    /// <param name="isTransientResponse">Predicate deciding whether a response is a transient failure worth retrying.</param>
    /// <param name="delayForAttempt">Delay before the next attempt given the current attempt number (1-based).</param>
    /// <param name="extraDelay">Optional extra delay applied after the normal delay (e.g. for 503).</param>
    /// <param name="isTransientException">Optional predicate deciding whether a caught exception is transient.</param>
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

        if (maxAttempts < MinAttempts)
        {
            maxAttempts = MinAttempts;
        }

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            var (response, lastException) = await TrySendAsync(sendFactory, cancellationToken).ConfigureAwait(false);

            if (response is not null && isTransientResponse(response) && attempt < maxAttempts)
            {
                await DelayForResponseAsync(response, attempt, delayForAttempt, extraDelay, cancellationToken).ConfigureAwait(false);
                continue;
            }

            if (lastException is not null && isTransientException is not null && isTransientException(lastException) && attempt < maxAttempts)
            {
                await DelayForExceptionAsync(lastException, attempt, delayForAttempt, cancellationToken).ConfigureAwait(false);
                continue;
            }

            // Success or non-transient or last attempt.
            return response ?? throw lastException ?? new InvalidOperationException("No response received.");
        }

        throw new InvalidOperationException("No response received.");
    }

    private static async Task<(HttpResponseMessage? Response, Exception? Exception)> TrySendAsync(
        Func<Task<HttpResponseMessage>> sendFactory,
        CancellationToken cancellationToken)
    {
        try
        {
            return (await sendFactory().ConfigureAwait(false), null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return (null, ex);
        }
    }

    private static async Task DelayForResponseAsync(
        HttpResponseMessage response,
        int attempt,
        Func<int, TimeSpan> delayForAttempt,
        Func<HttpResponseMessage, TimeSpan>? extraDelay,
        CancellationToken cancellationToken)
    {
        using (response)
        {
            var delay = delayForAttempt(attempt);
            if (extraDelay is not null)
            {
                delay += extraDelay(response);
            }

            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static async Task DelayForExceptionAsync(
        Exception ex,
        int attempt,
        Func<int, TimeSpan> delayForAttempt,
        CancellationToken cancellationToken)
    {
        var delay = delayForAttempt(attempt);
        if (delay > TimeSpan.Zero)
        {
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
        }
    }
}
