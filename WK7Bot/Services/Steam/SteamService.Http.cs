using System.Net.Http;
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
    private Task<HttpResponseMessage> GetSteamApiWithRetryAsync(string url, string operationName, CancellationToken cancellationToken)
    {
        return ExecuteWithRetryAsync(() => _httpClient.GetAsync(url, cancellationToken), operationName, cancellationToken);
    }
}
