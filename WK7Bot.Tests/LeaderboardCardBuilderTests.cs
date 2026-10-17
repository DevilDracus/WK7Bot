using Discord;
using WK7Bot.Core.Utilities;
using WK7Bot.Services.Interfaces;
using Xunit;

namespace WK7Bot.Tests;

public class LeaderboardCardBuilderTests
{
    [Fact]
    public void Build_RendersRankedRows()
    {
        var result = new LeaderboardResult(
            "⏱️ Steam-Spielzeit (14 Tage)",
            new List<LeaderboardEntry>
            {
                new(1, "Dracus", "5 Std.", "Dracus"),
                new(2, "Alex", "2 Std.", null)
            },
            "WK7 Bot • Leaderboard");

        var component = LeaderboardCardBuilder.Build(result);

        var container = Assert.IsType<ContainerComponent>(Assert.Single(component.Components));
        var text = string.Join(
            "\n",
            container.Components.OfType<TextDisplayComponent>().Select(display => display.Content));

        Assert.Contains("#1 Dracus", text);
        Assert.Contains("#2 Alex", text);
        Assert.Contains("WK7 Bot", text);
        Assert.Contains("Steam-Spielzeit", text);
    }

    [Fact]
    public void Build_UsesDividerBetweenTitleAndRows()
    {
        var result = new LeaderboardResult("Titel", new List<LeaderboardEntry> { new(1, "A", "1") });

        var component = LeaderboardCardBuilder.Build(result);

        var container = Assert.IsType<ContainerComponent>(Assert.Single(component.Components));
        Assert.Single(container.Components.OfType<SeparatorComponent>());
    }
}
