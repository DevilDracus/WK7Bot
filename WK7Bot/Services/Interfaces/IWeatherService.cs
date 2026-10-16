namespace WK7Bot.Services.Interfaces;

/// <summary>
/// Reads the current state and optional forecast of a Home Assistant weather entity.
/// </summary>
public interface IWeatherService
{
    /// <summary>
    /// Fetches the weather snapshot for the configured or supplied entity.
    /// </summary>
    /// <param name="entity">Optional entity override; defaults to <c>weather_entity</c>.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The weather snapshot, or <see langword="null"/> when the entity is unavailable.</returns>
    Task<WeatherSnapshot?> GetAsync(string? entity = null, CancellationToken cancellationToken = default);
}

/// <summary>
/// A weather snapshot read from a Home Assistant weather entity.
/// </summary>
/// <param name="EntityId">The source entity ID.</param>
/// <param name="FriendlyName">The entity friendly name.</param>
/// <param name="Condition">The current condition (for example <c>sunny</c>).</param>
/// <param name="Temperature">The current temperature in the entity's unit.</param>
/// <param name="Humidity">The current relative humidity percentage.</param>
/// <param name="WindSpeed">The current wind speed.</param>
/// <param name="Pressure">The current pressure.</param>
/// <param name="Forecast">Optional daily forecast entries.</param>
public sealed record WeatherSnapshot(
    string EntityId,
    string FriendlyName,
    string Condition,
    double? Temperature,
    double? Humidity,
    double? WindSpeed,
    double? Pressure,
    IReadOnlyList<WeatherForecastDay> Forecast);

/// <summary>
/// A single daily weather forecast entry.
/// </summary>
/// <param name="DateTime">The forecast date/time.</param>
/// <param name="Condition">The forecast condition.</param>
/// <param name="Temperature">The high temperature.</param>
/// <param name="TemperatureLow">The low temperature.</param>
public sealed record WeatherForecastDay(DateTime? DateTime, string Condition, double? Temperature, double? TemperatureLow);
