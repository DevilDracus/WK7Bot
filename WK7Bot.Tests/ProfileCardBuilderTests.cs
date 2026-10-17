using Discord;
using WK7Bot.Core.Utilities;
using WK7Bot.Services.Interfaces;
using Xunit;

namespace WK7Bot.Tests;

public class ProfileCardBuilderTests
{
    private static ProfileResult SampleProfile(string? avatarUrl = "https://example.com/avatar.png")
        => new(
            "Dracus",
            new SteamProfileSection(
                "Dracus",
                avatarUrl,
                "Online",
                "Baldurs Gate 3",
                300,
                "5 Std.",
                10,
                50,
                new List<SteamProfileGame>
                {
                    new("Baldurs Gate 3", 300, 6000, 10, 50)
                }),
            new BattleNetProfileSection(
                "Dracus#2123",
                "eu",
                new List<WowProfileCharacter>
                {
                    new("Lyria", "Blackrock", "Magier", "Allianz", 80, 620, "WK7", true)
                },
                new List<DiabloProfileHero>
                {
                    new("Zorn", "barbarian", 70, 800, true, false, false)
                }));

    [Fact]
    public void Build_ProducesSingleContainerWithTextDisplays()
    {
        var component = ProfileCardBuilder.Build(SampleProfile());

        var container = Assert.IsType<ContainerComponent>(Assert.Single(component.Components));
        Assert.Contains(container.Components, child => child is TextDisplayComponent);
    }

    [Fact]
    public void Build_IncludesAvatarAsSectionAccessory()
    {
        var component = ProfileCardBuilder.Build(SampleProfile());

        var container = Assert.IsType<ContainerComponent>(Assert.Single(component.Components));
        var section = Assert.IsType<SectionComponent>(container.Components.Single(child => child is SectionComponent));
        Assert.IsType<ThumbnailComponent>(section.Accessory);
    }

    [Fact]
    public void Build_WithoutAvatar_UsesPlainTextDisplay()
    {
        var component = ProfileCardBuilder.Build(SampleProfile(avatarUrl: null));

        var container = Assert.IsType<ContainerComponent>(Assert.Single(component.Components));
        Assert.DoesNotContain(container.Components, child => child is SectionComponent);
    }

    [Fact]
    public void Build_MentionsPersonaAndBattleTag()
    {
        var component = ProfileCardBuilder.Build(SampleProfile());

        var container = Assert.IsType<ContainerComponent>(Assert.Single(component.Components));
        var text = string.Join(
            "\n",
            container.Components.OfType<TextDisplayComponent>().Select(display => display.Content));

        Assert.Contains("Dracus", text);
        Assert.Contains("Baldurs Gate 3", text);
        Assert.Contains("Dracus#2123", text);
        Assert.Contains("Lyria", text);
        Assert.Contains("Zorn", text);
    }

    [Fact]
    public void Build_UsesSeparatorsBetweenSections()
    {
        var component = ProfileCardBuilder.Build(SampleProfile());

        var container = Assert.IsType<ContainerComponent>(Assert.Single(component.Components));
        Assert.Equal(2, container.Components.Count(child => child is SeparatorComponent));
    }
}
