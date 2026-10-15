using System.Net.Http.Headers;
using System.Net;
using System.Net.Http;
using Microsoft.Extensions.Logging;
namespace WK7Bot.Services;

public partial class BattleNetService
{

    /// <summary>
    /// Sends an authorized GET request under the shared API concurrency throttle.
    /// </summary>
    /// <param name="url">The fully qualified request URL.</param>
    /// <param name="accessToken">The bearer token attached to the request.</param>
    /// <param name="cancellationToken">A cancellation token to monitor for cancellation requests.</param>
    /// <returns>The received <see cref="HttpResponseMessage"/>.</returns>
    private Task<HttpResponseMessage> SendAuthorizedAsync(string url, string accessToken, CancellationToken cancellationToken)
    {
        return SendThrottledAsync(async () =>
        {
            // Dispose the request right after the send; the caller only owns the response.
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            return await _httpClient.SendAsync(request, cancellationToken);
        }, cancellationToken);
    }

    /// <summary>
    /// Executes a Battle.net API request while retrying transient failures (429/5xx) with linear backoff.
    /// </summary>
    /// <param name="sendFactory">Factory creating a fresh request per attempt (requests cannot be reused).</param>
    /// <param name="operationName">The logical Battle.net operation name used in log messages.</param>
    /// <param name="cancellationToken">A cancellation token to monitor for cancellation requests.</param>
    /// <returns>The final <see cref="HttpResponseMessage"/>; the caller owns and must dispose it.</returns>
    private Task<HttpResponseMessage> SendBnetApiWithRetryAsync(
        Func<Task<HttpResponseMessage>> sendFactory,
        string operationName,
        CancellationToken cancellationToken)
    {
        return ExecuteWithRetryAsync(sendFactory, operationName, cancellationToken);
    }
}
