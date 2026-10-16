namespace WK7Bot.Modules;

using Discord;
using Discord.Interactions;
using Microsoft.Extensions.Logging;
using WK7Bot.Services.Interfaces;

/// <summary>
/// Slash command that reports the current weather and optional forecast from a Home Assistant weather entity.
/// </summary>
public class WeatherModule : InteractionModuleBase<SocketInteractionContext>
{
    private readonly IWeatherService _weatherService;
    private readonly ILogger<WeatherModule> _logger;

    /// <summary>Initializes a new instance of the <see cref="WeatherModule"/> class.</summary>
    public WeatherModule(IWeatherService weatherService, ILogger<WeatherModule> logger)
    {
        _weatherService = weatherService ?? throw new ArgumentNullException(nameof(weatherService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>Posts the current weather for the configured entity.</summary>
    /// <param name="entity">Optional weather entity override.</param>
    [SlashCommand("weather", "Zeigt das aktuelle Wetter aus Home Assistant.")]
    public async Task WeatherAsync([Summary("entity", "Optional: Wetter-Entity, z. B. weather.home")] string? entity = null)
    {
        await DeferAsync(ephemeral: true);
        var snapshot = await _weatherService.GetAsync(entity);
        if (snapshot is null)
        {
            await FollowupAsync("❌ Wetterdaten konnten nicht geladen werden. Ist die Entity korrekt konfiguriert?", ephemeral: true);
            return;
        }

        var embed = new EmbedBuilder()
            .WithTitle($"🌤️ {snapshot.FriendlyName}")
            .WithDescription($"Aktuell: **{snapshot.Condition}**")
            .WithColor(Color.Blue)
            .WithTimestamp(DateTimeOffset.UtcNow)
            .WithFooter("WK7 Bot • Home Assistant");

        if (snapshot.Temperature.HasValue) embed.AddField("Temperatur", $"{snapshot.Temperature.Value:0.#} °C", inline: true);
        if (snapshot.Humidity.HasValue) embed.AddField("Luftfeuchte", $"{snapshot.Humidity.Value:0} %", inline: true);
        if (snapshot.WindSpeed.HasValue) embed.AddField("Wind", $"{snapshot.WindSpeed.Value:0.#} km/h", inline: true);
        if (snapshot.Pressure.HasValue) embed.AddField("Luftdruck", $"{snapshot.Pressure.Value:0} hPa", inline: true);

        if (snapshot.Forecast.Count > 0)
        {
            var lines = snapshot.Forecast.Take(3).Select(day =>
                $"{day.DateTime:ddd dd.MM.} — {day.Condition}" +
                (day.Temperature.HasValue ? $", {day.Temperature.Value:0.#}°" : string.Empty) +
                (day.TemperatureLow.HasValue ? $"/{day.TemperatureLow.Value:0.#}°" : string.Empty));
            embed.AddField("Vorhersage", string.Join("\n", lines), inline: false);
        }

        await FollowupAsync(embed: embed.Build(), ephemeral: true);
    }
}
