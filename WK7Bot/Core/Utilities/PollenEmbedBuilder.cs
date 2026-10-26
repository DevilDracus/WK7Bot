namespace WK7Bot.Core.Utilities;

using System.Globalization;
using System.Text;
using Discord;
using WK7Bot.Models;

/// <summary>
/// Builds the German pollen/UV embed shared by the <c>/pollen</c> command and the daily morning post.
/// </summary>
public static class PollenEmbedBuilder
{
    /// <summary>Builds the embed from the pollen and (optional) UV forecast.</summary>
    /// <param name="pollen">The pollen forecast, or <see langword="null"/> when unavailable.</param>
    /// <param name="uv">The UV forecast, or <see langword="null"/> when unavailable.</param>
    /// <returns>The embed, or <see langword="null"/> when both inputs are missing.</returns>
    public static Embed? Build(PollenForecast? pollen, UvForecast? uv)
    {
        if (pollen is null && uv is null)
        {
            return null;
        }

        var builder = new EmbedBuilder()
            .WithColor(Color.Green)
            .WithCurrentTimestamp()
            .WithFooter("Datenquelle: DWD (CC BY 4.0)");

        if (pollen is not null)
        {
            var region = string.IsNullOrWhiteSpace(pollen.PartRegionName) ? pollen.RegionName : pollen.PartRegionName;
            builder.WithTitle($"🌾 Pollenflug — {region}");

            var description = new StringBuilder("Gefahrenindex für heute, morgen und übermorgen.");
            if (pollen.LastUpdate.HasValue)
            {
                description.Append($"\nStand: <t:{pollen.LastUpdate.Value.ToUnixTimeSeconds()}:f>");
            }

            builder.WithDescription(description.ToString());

            var loaded = pollen.Types.Where(type => type.Today.HasLoad || type.Tomorrow.HasLoad || type.DayAfterTomorrow.HasLoad).ToList();

            if (loaded.Count == 0)
            {
                builder.AddField("Belastung", "Heute, morgen und übermorgen ist keine relevante Belastung vorhergesagt.", inline: false);
            }
            else
            {
                foreach (var type in loaded)
                {
                    var value = $"Heute: {type.Today.Display()}\nMorgen: {type.Tomorrow.Display()}\nÜbermorgen: {type.DayAfterTomorrow.Display()}";
                    builder.AddField(type.Name, value, inline: true);
                }
            }
        }

        if (uv is not null)
        {
            var uvText = string.Create(
                CultureInfo.InvariantCulture,
                $"Heute: **{uv.Today}** · Morgen: {uv.Tomorrow} · Übermorgen: {uv.DayAfterTomorrow}");

            builder.AddField($"☀️ UV-Index {uv.City}", $"{uvText}\n{DescribeUv(uv.Today)}", inline: false);
        }

        return builder.Build();
    }

    /// <summary>Returns the German DWD wording for a UV index value.</summary>
    /// <param name="index">The UV index.</param>
    /// <returns>The description.</returns>
    private static string DescribeUv(int index) => index switch
    {
        <= 2 => "keine bis geringe Belastung",
        3 or 4 => "mittlere Belastung",
        5 or 6 => "hohe Belastung",
        7 or 8 => "sehr hohe Belastung",
        _ => "extrem hohe Belastung"
    };
}
