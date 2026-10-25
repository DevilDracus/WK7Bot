namespace WK7Bot.Services;

using Microsoft.Extensions.Options;
using WK7Bot.Core.Utilities;
using WK7Bot.Models;
using WK7Bot.Options;
using WK7Bot.Services.Interfaces;

/// <summary>
/// Fetches the DWD pollen and UV index feeds through the shared DWD resilience policy (retry plus
/// circuit breaker), mirroring how the CAP warning service treats the same host.
/// </summary>
public class PollenService : IPollenService
{
    /// <summary>Pollen hazard index for today, tomorrow and the day after tomorrow.</summary>
    public const string PollenFeedUrl = "https://opendata.dwd.de/climate_environment/health/alerts/s31fg.json";

    /// <summary>UV index forecast per city.</summary>
    public const string UvFeedUrl = "https://opendata.dwd.de/climate_environment/health/alerts/uvi.json";

    private readonly HttpClient _httpClient;
    private readonly DwdResiliencePolicy _policy;
    private readonly Wk7BotOptions _options;
    private readonly ILogger<PollenService> _logger;

    /// <summary>Initializes a new instance of the <see cref="PollenService"/> class.</summary>
    /// <param name="httpClient">The HTTP client used for the DWD feeds.</param>
    /// <param name="policy">The shared DWD retry/circuit-breaker policy.</param>
    /// <param name="options">The strongly-typed bot options.</param>
    /// <param name="logger">The logger instance.</param>
    public PollenService(
        HttpClient httpClient,
        DwdResiliencePolicy policy,
        IOptions<Wk7BotOptions> options,
        ILogger<PollenService> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _policy = policy ?? throw new ArgumentNullException(nameof(policy));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<PollenForecast?> GetPollenAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var json = await _policy.ExecuteAsync(
                token => _httpClient.GetStringAsync(PollenFeedUrl, token),
                cancellationToken).ConfigureAwait(false);

            return PollenFeedParser.Parse(json, _options.PollenRegionName, _options.PollenPartRegionName);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Could not fetch or parse the DWD pollen feed.");
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<UvForecast?> GetUvAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var json = await _policy.ExecuteAsync(
                token => _httpClient.GetStringAsync(UvFeedUrl, token),
                cancellationToken).ConfigureAwait(false);

            return PollenFeedParser.ParseUv(json, _options.UvCity);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Could not fetch or parse the DWD UV index feed.");
            return null;
        }
    }
}
