namespace WK7Bot.Core.Utilities;

using System.Text;
using Discord;
using WK7Bot.Services.Interfaces;

/// <summary>
/// Renders a leaderboard as a Discord Components V2 card: one container with the title, a divider and
/// one text block holding the ranked rows.
/// </summary>
public static class LeaderboardCardBuilder
{
    /// <summary>Discord's per-text-display character ceiling.</summary>

    /// <summary>Accent colour of the leaderboard container.</summary>
    private static readonly Color AccentColor = new(0xF1, 0xC4, 0x0F);

    /// <summary>Builds the leaderboard card.</summary>
    /// <param name="result">The ranked leaderboard result.</param>
    /// <returns>The message components to send with <see cref="MessageFlags.ComponentsV2"/>.</returns>
    public static MessageComponent Build(LeaderboardResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        var rows = new StringBuilder();
        foreach (var entry in result.Entries)
        {
            rows.Append($"**#{entry.Rank} {entry.DisplayName}** — {entry.Value}");
            if (!string.IsNullOrWhiteSpace(entry.Detail))
            {
                rows.Append($" · {entry.Detail}");
            }

            rows.AppendLine();
        }

        var footer = string.IsNullOrWhiteSpace(result.Footer) ? string.Empty : $"\n-# {result.Footer}";
        var body = EmbedText.TruncateHard(rows.ToString().TrimEnd() + footer, CardBuilder.MaxTextDisplayLength);

        return new ComponentBuilderV2()
            .AddComponent(new ContainerBuilder()
                .WithAccentColor(AccentColor)
                .AddComponent(new TextDisplayBuilder().WithContent(EmbedText.TruncateHard($"# {result.Title}", CardBuilder.MaxTextDisplayLength)))
                .AddComponent(new SeparatorBuilder().WithIsDivider(true).WithSpacing(SeparatorSpacingSize.Small))
                .AddComponent(new TextDisplayBuilder().WithContent(body)))
            .Build();
    }

    /// <summary>Clamps text to Discord's per-text-display character ceiling.</summary>
    /// <param name="text">The text to clamp.</param>
    /// <returns>The clamped text.</returns>
}
