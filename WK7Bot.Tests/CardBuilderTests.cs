using Discord;
using WK7Bot.Core.Utilities;
using Xunit;

namespace WK7Bot.Tests;

public class CardBuilderTests
{
    private static ContainerComponent Container(MessageComponent card)
        => Assert.IsType<ContainerComponent>(Assert.Single(card.Components));

    private static string AllText(ContainerComponent container)
        => string.Join(
            "\n",
            container.Components.OfType<TextDisplayComponent>().Select(display => display.Content));

    [Fact]
    public void FromEmbed_UsesTheEmbedColourAsAccent()
    {
        var embed = new EmbedBuilder().WithColor(new Color(0x12, 0x34, 0x56)).WithTitle("Titel").Build();

        var container = Container(CardBuilder.FromEmbed(embed));

        Assert.Equal(new Color(0x12, 0x34, 0x56), container.AccentColor);
    }

    [Fact]
    public void FromEmbed_UsesDefaultAccentWhenTheEmbedHasNoColour()
    {
        var container = Container(CardBuilder.FromEmbed(new EmbedBuilder().WithTitle("Titel").Build()));

        Assert.Equal(CardBuilder.DefaultAccent, container.AccentColor);
    }

    [Fact]
    public void FromEmbed_RendersTitleAsHeadingDescriptionAndFields()
    {
        var embed = new EmbedBuilder()
            .WithTitle("Pollenflug")
            .WithDescription("Gefahrenindex für heute.")
            .AddField("Gräser", "Heute: 2", inline: true)
            .Build();

        var text = AllText(Container(CardBuilder.FromEmbed(embed)));

        Assert.Contains("# Pollenflug", text);
        Assert.Contains("Gefahrenindex für heute.", text);
        Assert.Contains("**Gräser**", text);
        Assert.Contains("Heute: 2", text);
    }

    [Fact]
    public void FromEmbed_PairsThumbnailWithDescriptionAsSectionAccessory()
    {
        var embed = new EmbedBuilder()
            .WithDescription("Beschreibung")
            .WithThumbnailUrl("https://example.com/thumb.png")
            .Build();

        var container = Container(CardBuilder.FromEmbed(embed));
        var section = Assert.IsType<SectionComponent>(Assert.Single(container.Components));

        Assert.Equal("https://example.com/thumb.png", Assert.IsType<ThumbnailComponent>(section.Accessory).Media.Url);
        Assert.Contains("Beschreibung", Assert.IsType<TextDisplayComponent>(Assert.Single(section.Components)).Content);
    }

    [Fact]
    public void FromEmbed_AddsStandaloneThumbnailWithoutDescription()
    {
        var embed = new EmbedBuilder().WithThumbnailUrl("https://example.com/thumb.png").Build();

        var container = Container(CardBuilder.FromEmbed(embed));
        var section = Assert.IsType<SectionComponent>(Assert.Single(container.Components));

        Assert.Equal("https://example.com/thumb.png", Assert.IsType<ThumbnailComponent>(section.Accessory).Media.Url);
    }

    [Fact]
    public void FromEmbed_ConvertsTheImageIntoAMediaGallery()
    {
        var embed = new EmbedBuilder().WithImageUrl("https://example.com/big.png").Build();

        var gallery = Assert.IsType<MediaGalleryComponent>(Assert.Single(Container(CardBuilder.FromEmbed(embed)).Components));

        Assert.Equal("https://example.com/big.png", Assert.Single(gallery.Items).Media.Url);
    }

    [Fact]
    public void FromEmbed_KeepsTheTitleLink()
    {
        var embed = new EmbedBuilder().WithTitle("Deal").WithUrl("https://store.example/deal").Build();

        var text = AllText(Container(CardBuilder.FromEmbed(embed)));

        Assert.Contains("https://store.example/deal", text);
    }

    [Fact]
    public void FromEmbed_RendersFooterAndTimestamp()
    {
        var embed = new EmbedBuilder()
            .WithFooter("WK7 Bot • Deals")
            .WithTimestamp(DateTimeOffset.UnixEpoch)
            .Build();

        var text = AllText(Container(CardBuilder.FromEmbed(embed)));

        Assert.Contains("WK7 Bot • Deals", text);
        Assert.Contains("<t:0:R>", text);
    }

    [Fact]
    public void FromEmbed_ConsolidatesManyFieldsToRespectTheChildLimit()
    {
        var builder = new EmbedBuilder().WithTitle("Viele Felder");
        for (var i = 1; i <= 25; i++)
        {
            builder.AddField($"Feld {i}", $"Wert {i}");
        }

        var container = Container(CardBuilder.FromEmbed(builder.Build()));

        Assert.True(
            container.Components.Count <= CardBuilder.MaxChildrenPerContainer,
            $"container has {container.Components.Count} children");
        for (var i = 1; i <= 25; i++)
        {
            Assert.Contains($"Feld {i}", AllText(container));
        }
    }

    [Fact]
    public void FromEmbed_RespectsTheTotalTextBudget()
    {
        var embed = new EmbedBuilder()
            .WithDescription(new string('a', 3500))
            .AddField("Feld", new string('b', 1024))
            .Build();

        var container = Container(CardBuilder.FromEmbed(embed));
        var total = container.Components.OfType<TextDisplayComponent>().Sum(display => display.Content.Length);

        Assert.True(total <= CardBuilder.MaxTotalTextLength, $"total text length was {total}");
    }

    [Fact]
    public void Build_RendersSectionsAndFooter()
    {
        var card = CardBuilder.Build(
            "Titel",
            "Einleitung",
            new List<(string, string)> { ("Abschnitt", "Inhalt") },
            Color.Blue,
            "Fußzeile");

        var container = Container(card);
        var text = AllText(container);

        Assert.Contains("# Titel", text);
        Assert.Contains("Einleitung", text);
        Assert.Contains("**Abschnitt**", text);
        Assert.Contains("-# Fußzeile", text);
        Assert.Equal(Color.Blue, container.AccentColor);
    }
}
