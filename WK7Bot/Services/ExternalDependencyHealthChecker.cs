namespace WK7Bot.Services;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WK7Bot.Core.Utilities;
using WK7Bot.Options;
using WK7Bot.Services.Interfaces;

/// <summary>
/// Checks the health of external API dependencies used by the bot.
/// </summary>
public sealed class ExternalDependencyHealthChecker : IExternalDependencyHealthChecker
{
    private readonly IHomeAssistantService _homeAssistantService;
    private readonly IOptions<Wk7BotOptions> _options;
    private readonly ILogger<ExternalDependencyHealthChecker> _logger;
    private readonly HttpClient _httpClient;
    private readonly MetricsRegistry _metrics;

    /// <summary>
    /// Initializes a new instance of the <see cref="ExternalDependencyHealthChecker"/> class.
    /// </summary>
    /// <param name="homeAssistantService">The Home Assistant API client.</param>
    /// <param name="options">The bot options.</param>
    /// <param name="logger">The logger instance.</param>
    /// <param name="httpClient">The HTTP client for dependency checks.</param>
    /// <param name="metrics">The metrics registry.</param>
    public ExternalDependencyHealthChecker(
        IHomeAssistantService homeAssistantService,
        IOptions<Wk7BotOptions> options,
        ILogger<ExternalDependencyHealthChecker> logger,
        HttpClient httpClient,
        MetricsRegistry metrics)
    {
        _homeAssistantService = homeAssistantService ?? throw new ArgumentNullException(nameof(homeAssistantService));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DependencyHealthStatus>> CheckAllAsync(CancellationToken cancellationToken = default)
    {
        var results = await Task.WhenAll(
            CheckHomeAssistantAsync(cancellationToken),
            CheckDwdAsync(cancellationToken),
            CheckSteamAsync(cancellationToken),
            CheckGeminiAsync(cancellationToken),
            CheckOpenFoodFactsAsync(cancellationToken),
            CheckIsThereAnyDealAsync(cancellationToken)).ConfigureAwait(false);

        // Project the per-dependency outcome into the Prometheus registry here instead of inside each
        // check: the fan-out is the single place that sees every result, so one loop instruments all
        // six dependencies (including the key-only Gemini check, which reports no latency).
        foreach (var result in results)
        {
            _metrics.RecordHttp(
                $"HealthCheck:{result.Name}",
                result.Latency?.TotalMilliseconds ?? 0,
                result.IsHealthy);
        }

        return results;
    }

    private async Task<DependencyHealthStatus> CheckHomeAssistantAsync(CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            string? response = await _homeAssistantService.GetApiStateAsync("config", cancellationToken).ConfigureAwait(false);
            sw.Stop();

            if (response != null)
            {
                return new DependencyHealthStatus("Home Assistant", true, "Supervisor API erreichbar", sw.Elapsed);
            }

            return new DependencyHealthStatus("Home Assistant", false, "Keine Antwort vom Supervisor", sw.Elapsed);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            sw.Stop();
            _logger.LogDebug(ex, "Home Assistant health check failed.");
            return new DependencyHealthStatus("Home Assistant", false, ex.Message, sw.Elapsed);
        }
    }

    private async Task<DependencyHealthStatus> CheckDwdAsync(CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        try
        {

            using var response = await _httpClient.GetAsync(
                "https://opendata.dwd.de/weather/alerts/cap/COMMUNEUNION_DWD_STAT/",
                cancellationToken).ConfigureAwait(false);

            sw.Stop();

            if (response.IsSuccessStatusCode)
            {
                return new DependencyHealthStatus("DWD Wetterwarnungen", true, "CAP-Feed erreichbar", sw.Elapsed);
            }

            return new DependencyHealthStatus("DWD Wetterwarnungen", false, $"HTTP {(int)response.StatusCode}", sw.Elapsed);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            sw.Stop();
            _logger.LogDebug(ex, "DWD health check failed.");
            return new DependencyHealthStatus("DWD Wetterwarnungen", false, ex.Message, sw.Elapsed);
        }
    }

    private async Task<DependencyHealthStatus> CheckSteamAsync(CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        try
        {

            using var response = await _httpClient.GetAsync(
                "https://store.steampowered.com/api/appdetails?appids=620",
                cancellationToken).ConfigureAwait(false);

            sw.Stop();

            if (response.IsSuccessStatusCode)
            {
                return new DependencyHealthStatus("Steam", true, "Store API erreichbar", sw.Elapsed);
            }

            return new DependencyHealthStatus("Steam", false, $"HTTP {(int)response.StatusCode}", sw.Elapsed);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            sw.Stop();
            _logger.LogDebug(ex, "Steam health check failed.");
            return new DependencyHealthStatus("Steam", false, ex.Message, sw.Elapsed);
        }
    }

    private Task<DependencyHealthStatus> CheckGeminiAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.Value.GeminiApiKey))
        {
            return Task.FromResult(new DependencyHealthStatus("Gemini", false, "API-Key fehlt"));
        }

        return Task.FromResult(new DependencyHealthStatus("Gemini", true, "API-Key konfiguriert"));
    }

    private async Task<DependencyHealthStatus> CheckOpenFoodFactsAsync(CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        try
        {

            using var response = await _httpClient.GetAsync(
                "https://world.openfoodfacts.org/cgi/search.pl?search_terms=banana&json=1&page_size=1",
                cancellationToken).ConfigureAwait(false);

            sw.Stop();

            if (response.IsSuccessStatusCode)
            {
                return new DependencyHealthStatus("Open Food Facts", true, "API erreichbar", sw.Elapsed);
            }

            return new DependencyHealthStatus("Open Food Facts", false, $"HTTP {(int)response.StatusCode}", sw.Elapsed);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            sw.Stop();
            _logger.LogDebug(ex, "Open Food Facts health check failed.");
            return new DependencyHealthStatus("Open Food Facts", false, ex.Message, sw.Elapsed);
        }
    }

    private async Task<DependencyHealthStatus> CheckIsThereAnyDealAsync(CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        try
        {

            using var response = await _httpClient.GetAsync(
                "https://api.isthereanydeal.com/v01/web/regions/",
                cancellationToken).ConfigureAwait(false);

            sw.Stop();

            if (response.IsSuccessStatusCode)
            {
                return new DependencyHealthStatus("IsThereAnyDeal", true, "API erreichbar", sw.Elapsed);
            }

            return new DependencyHealthStatus("IsThereAnyDeal", false, $"HTTP {(int)response.StatusCode}", sw.Elapsed);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            sw.Stop();
            _logger.LogDebug(ex, "IsThereAnyDeal health check failed.");
            return new DependencyHealthStatus("IsThereAnyDeal", false, ex.Message, sw.Elapsed);
        }
    }
}
