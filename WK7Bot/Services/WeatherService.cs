namespace WK7Bot.Services;

using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Options;
using WK7Bot.Options;
using WK7Bot.Services.Interfaces;

/// <summary>
/// Reads weather data from a Home Assistant weather entity through the Supervisor REST API.
/// </summary>
public class WeatherService : IWeatherService
{
    private readonly IHomeAssistantService _homeAssistantService;
    private readonly IOptions<Wk7BotOptions> _options;
    private readonly ILogger<WeatherService> _logger;

    /// <summary>Initializes a new instance of the <see cref="WeatherService"/> class.</summary>
    public WeatherService(IHomeAssistantService homeAssistantService, IOptions<Wk7BotOptions> options, ILogger<WeatherService> logger)
    {
        _homeAssistantService = homeAssistantService ?? throw new ArgumentNullException(nameof(homeAssistantService));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<WeatherSnapshot?> GetAsync(string? entity = null, CancellationToken cancellationToken = default)
    {
        var entityId = string.IsNullOrWhiteSpace(entity) ? _options.Value.WeatherEntity : entity!.Trim();
        if (string.IsNullOrWhiteSpace(entityId))
        {
            return null;
        }

        var payload = await _homeAssistantService.GetApiStateAsync($"states/{entityId}", cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(payload))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;
            var condition = root.TryGetProperty("state", out var state) ? state.GetString() ?? "unknown" : "unknown";
            var attributes = root.TryGetProperty("attributes", out var attrs) ? attrs : default;
            var friendlyName = attributes.ValueKind == JsonValueKind.Object && attributes.TryGetProperty("friendly_name", out var name)
                ? name.GetString() ?? entityId
                : entityId;

            var forecast = new List<WeatherForecastDay>();
            if (attributes.ValueKind == JsonValueKind.Object && attributes.TryGetProperty("forecast", out var forecastArray) && forecastArray.ValueKind == JsonValueKind.Array)
            {
                foreach (var day in forecastArray.EnumerateArray())
                {
                    forecast.Add(new WeatherForecastDay(
                        day.TryGetProperty("datetime", out var dt) && DateTime.TryParse(dt.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out var parsed) ? parsed : null,
                        day.TryGetProperty("condition", out var c) ? c.GetString() ?? "unknown" : "unknown",
                        ReadDouble(day, "temperature"),
                        ReadDouble(day, "templow")));
                }
            }

            return new WeatherSnapshot(
                entityId,
                friendlyName,
                condition,
                ReadDouble(attributes, "temperature"),
                ReadDouble(attributes, "humidity"),
                ReadDouble(attributes, "wind_speed"),
                ReadDouble(attributes, "pressure"),
                forecast);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Could not parse weather entity {Entity}.", entityId);
            return null;
        }
    }

    private static double? ReadDouble(JsonElement element, string property)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(property, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number => value.GetDouble(),
            JsonValueKind.String when double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => null
        };
    }
}
