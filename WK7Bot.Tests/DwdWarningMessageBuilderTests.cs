using Discord;
using WK7Bot.Core.Utilities;
using WK7Bot.Models;
using WK7Bot.Options;
using Xunit;

namespace WK7Bot.Tests;

public class DwdWarningMessageBuilderTests
{
    private static readonly DateTime PostTime = new(2026, 10, 6, 13, 0, 0);

    private static DwdWarningOptions Options()
        => new()
        {
            PostalCode = "04205",
            Latitude = 51.34,
            Longitude = 12.36,
            AreaNames = new List<string> { "Stadt Leipzig" }
        };

    private static CapWarning Warning(DateTime postTime)
        => new()
        {
            Identifier = "warn-1",
            Event = "STARKREGEN",
            EventGroup = "HEAVY RAIN",
            Severity = "Severe",
            Headline = "Amtliche WARNUNG vor STARKREGEN",
            Description = "Es tritt starker Regen mit über 25 l/m² in kurzer Zeit auf.",
            Instruction = "Balkonfenster schließen und unterlassene Fahrten einplanen.",
            Web = "https://dwd.de/warnungen",
            Onset = postTime.AddMinutes(20),
            Expires = new DateTime(2026, 10, 6, 16, 0, 0),
            AreaDescriptions = new List<string> { "polygonal event area", "Stadt Leipzig" },
            Polygons = new List<IReadOnlyList<GeoPoint>> { new List<GeoPoint>() },
            ExcludePolygons = new List<IReadOnlyList<GeoPoint>>()
        };

    private static string? FieldValue(Embed embed, string name)
        => embed.Fields.Where(f => f.Name == name).Select(f => f.Value).FirstOrDefault();

    [Fact]
    public void Build_RendersTitleDescriptionFooterAndSource()
    {
        var embed = DwdWarningMessageBuilder.Build(Warning(PostTime), PostTime, Options());

        Assert.Equal("⚠️ STARKREGEN für Stadt Leipzig", embed.Title);
        Assert.Equal("Amtliche WARNUNG vor STARKREGEN", embed.Description);
        Assert.Equal(Color.Orange, embed.Color);
        Assert.Equal("Deutscher Wetterdienst (DWD) • PLZ 04205", embed.Footer?.Text);
        Assert.Equal("https://dwd.de/warnungen", embed.Url);
        Assert.Equal(PostTime, embed.Timestamp!.Value.DateTime);
    }

    [Fact]
    public void Build_FutureOnset_ShowsCountdown()
    {
        var embed = DwdWarningMessageBuilder.Build(Warning(PostTime), PostTime, Options());

        var onset = FieldValue(embed, "🕐 Eintritt");
        Assert.NotNull(onset);
        Assert.StartsWith("ab 13:20 Uhr", onset);
        Assert.Contains("in 20 Min.", onset);
    }

    [Fact]
    public void Build_PastOnset_ShowsRunningSince()
    {
        var warning = Warning(PostTime);
        warning.Onset = PostTime.AddMinutes(-3);

        var embed = DwdWarningMessageBuilder.Build(warning, PostTime, Options());

        var onset = FieldValue(embed, "🕐 Eintritt");
        Assert.NotNull(onset);
        Assert.StartsWith("seit 12:57 Uhr", onset);
        Assert.DoesNotContain("in ", onset);
    }

    [Fact]
    public void Build_LongOnsetDuration_UsesHours()
    {
        var warning = Warning(PostTime);
        warning.Onset = PostTime.AddMinutes(125);

        var embed = DwdWarningMessageBuilder.Build(warning, PostTime, Options());

        var onset = FieldValue(embed, "🕐 Eintritt");
        Assert.NotNull(onset);
        Assert.Contains("in 2 Std. 5 Min.", onset);
    }

    [Fact]
    public void Build_ExpiryOnSameDay_UsesTimeOnly()
    {
        var embed = DwdWarningMessageBuilder.Build(Warning(PostTime), PostTime, Options());

        Assert.Equal("bis 16:00 Uhr", FieldValue(embed, "⏳ Gültig bis"));
    }

    [Fact]
    public void Build_ExpiryOnAnotherDay_IncludesDate()
    {
        var warning = Warning(PostTime);
        warning.Expires = new DateTime(2026, 10, 7, 9, 30, 0);

        var embed = DwdWarningMessageBuilder.Build(warning, PostTime, Options());

        Assert.Equal("bis 07.10., 09:30 Uhr", FieldValue(embed, "⏳ Gültig bis"));
    }

    [Fact]
    public void Build_MissingExpiry_OmitsTheField()
    {
        var warning = Warning(PostTime);
        warning.Expires = DateTime.MaxValue;

        var embed = DwdWarningMessageBuilder.Build(warning, PostTime, Options());

        Assert.DoesNotContain(embed.Fields, f => f.Name == "⏳ Gültig bis");
    }

    [Fact]
    public void Build_AreaField_CountsAdditionalPlaces()
    {
        var warning = Warning(PostTime);
        warning.AreaDescriptions = new List<string> { "polygonal event area", "Stadt Leipzig", "Gemeinde Möckern" };

        var embed = DwdWarningMessageBuilder.Build(warning, PostTime, Options());

        Assert.Equal("Stadt Leipzig (+1 weitere Orte)", FieldValue(embed, "📍 Gebiet"));
    }

    [Theory]
    [InlineData("Extreme", "🔴 Extrem")]
    [InlineData("Severe", "🟠 Schwer")]
    [InlineData("Moderate", "🟡 Mittel")]
    [InlineData("Minor", "🔵 Gering")]
    public void Build_SeverityLabels(string severity, string expected)
    {
        var warning = Warning(PostTime);
        warning.Severity = severity;

        var embed = DwdWarningMessageBuilder.Build(warning, PostTime, Options());

        Assert.Equal(expected, FieldValue(embed, "⚠️ Schweregrad"));
    }

    [Theory]
    [InlineData("Extreme", 231, 76, 60)]
    [InlineData("Severe", 230, 126, 34)]
    [InlineData("Moderate", 241, 196, 15)]
    [InlineData("Minor", 88, 101, 242)]
    public void Build_SeverityColors(string severity, byte red, byte green, byte blue)
    {
        var warning = Warning(PostTime);
        warning.Severity = severity;

        var embed = DwdWarningMessageBuilder.Build(warning, PostTime, Options());

        Assert.Equal(new Color(red, green, blue), embed.Color);
    }

    [Fact]
    public void Build_LongEvent_TruncatesTitleToDiscordLimit()
    {
        var warning = Warning(PostTime);
        warning.Event = new string('X', 300);

        var embed = DwdWarningMessageBuilder.Build(warning, PostTime, Options());

        Assert.NotNull(embed.Title);
        Assert.True(embed.Title!.Length <= 256, $"title length {embed.Title.Length}");
        Assert.EndsWith("…", embed.Title);
    }

    [Fact]
    public void Build_LongDescription_IsTruncatedToFieldLimit()
    {
        var warning = Warning(PostTime);
        warning.Description = new string('Y', 2000);

        var embed = DwdWarningMessageBuilder.Build(warning, PostTime, Options());

        var description = FieldValue(embed, "📝 Beschreibung");
        Assert.NotNull(description);
        Assert.True(description!.Length <= 1024, $"description length {description.Length}");
        Assert.EndsWith("…", description);
    }

    [Fact]
    public void Build_LocationFallsBackToFirstNamedArea_WhenNoConfiguredNameMatches()
    {
        var options = Options();
        options.AreaNames = new List<string>();
        var warning = Warning(PostTime);
        warning.AreaDescriptions = new List<string> { "polygonal event area", "Stadt Gohlis", "Gemeinde Möckern" };

        var embed = DwdWarningMessageBuilder.Build(warning, PostTime, options);

        Assert.Equal("⚠️ STARKREGEN für Stadt Gohlis", embed.Title);
        Assert.Equal("Stadt Gohlis (+1 weitere Orte)", FieldValue(embed, "📍 Gebiet"));
    }

    [Fact]
    public void Build_LocationFallsBackToPostalCode_WhenWarningHasNoNamedArea()
    {
        var options = Options();
        options.AreaNames = new List<string>();
        var warning = Warning(PostTime);
        warning.AreaDescriptions = new List<string> { "polygonal event area" };

        var embed = DwdWarningMessageBuilder.Build(warning, PostTime, options);

        Assert.Equal("⚠️ STARKREGEN für PLZ 04205", embed.Title);
    }

    [Fact]
    public void Build_WithoutPostalCode_FooterOmitsPlz()
    {
        var options = Options();
        options.PostalCode = string.Empty;

        var embed = DwdWarningMessageBuilder.Build(Warning(PostTime), PostTime, options);

        Assert.Equal("Deutscher Wetterdienst (DWD)", embed.Footer?.Text);
    }

    [Fact]
    public void Build_WithoutWeb_OmitsUrl()
    {
        var warning = Warning(PostTime);
        warning.Web = string.Empty;

        var embed = DwdWarningMessageBuilder.Build(warning, PostTime, Options());

        Assert.Null(embed.Url);
    }

    [Fact]
    public void Build_WithoutHeadlineDescriptionAndInstruction_OmitsDescriptionAndActionFields()
    {
        var warning = Warning(PostTime);
        warning.Headline = string.Empty;
        warning.Description = string.Empty;
        warning.Instruction = string.Empty;

        var embed = DwdWarningMessageBuilder.Build(warning, PostTime, Options());

        Assert.DoesNotContain(embed.Fields, f => f.Name == "📝 Beschreibung");
        Assert.DoesNotContain(embed.Fields, f => f.Name == "✅ Empfehlung");
        Assert.Null(embed.Description);
    }
}
