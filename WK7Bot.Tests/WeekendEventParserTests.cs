using WK7Bot.Core.Utilities;
using Xunit;

namespace WK7Bot.Tests;

public class WeekendEventParserTests
{
    private const string FullCardHtml = """
    <li data-pid="17545" data-event="2775941" data-index="16044025">
      <article class="card event-card">
        <a class="link d-flex" href="/kultur-und-freizeit/veranstaltungen/eventsingle/event/wochenmarkt-in-der-innenstadt">
          <div class="card-body event-card-body">
            <h3 class="h3 card-title font-interactive-2 mb-2">Wochenmarkt in der Innenstadt</h3>
            <div class="d-flex gap-2 flex-column">
              <span class="icon-text"> <span class="icon" aria-hidden="true">event</span> <span> 10.10.2026 &middot; 10:00 &ndash; 16:00 Uhr </span> </span>
              <span class="icon-text"> <span class="icon" aria-hidden="true">location_on</span> <span> Richard-Wagner-Platz </span> </span>
              <span class="icon-text"> <span class="icon" aria-hidden="true">topic</span> <span> <span> Messen </span> </span> </span>
            </div>
          </div>
        </a>
      </article>
    </li>
    """;

    private const string MinimalCardHtml = """
    <li data-pid="17545" data-event="2775942" data-index="16044026">
      <article class="card event-card">
        <a class="link" href="/kultur-und-freizeit/veranstaltungen/eventsingle/event/sonderfuehrung">
          <div class="card-body">
            <h3 class="card-title">Sonderf&uuml;hrung: Herbst &amp; Geschichte</h3>
            <div>
              <span class="icon-text"> <span class="icon" aria-hidden="true">event</span> <span> 09.10.2026 &middot; 10:00 </span> </span>
              <span class="icon-text"> <span class="icon" aria-hidden="true">location_on</span> <span> Zeitgeschichtliches Forum </span> </span>
            </div>
          </div>
        </a>
      </article>
    </li>
    """;

    private const string MultiDayCardHtml = """
    <li data-pid="28214" data-event="2754982" data-index="14998710">
      <article class="card event-card">
        <a class="link" href="/kultur-und-freizeit/veranstaltungen/eventsingle/event/europacup-im-speedklettern-2026">
          <div class="card-body">
            <h3 class="card-title">Europacup im Speedklettern 2026</h3>
            <div>
              <span class="icon-text"> <span class="icon" aria-hidden="true">event</span> <span> 03.10.2026 &ndash; 04.10.2026 </span> </span>
              <span class="icon-text"> <span class="icon" aria-hidden="true">location_on</span> <span> Leipziger Messe </span> </span>
            </div>
          </div>
        </a>
      </article>
    </li>
    """;

    private const string DateOnlyCardHtml = """
    <li data-pid="1" data-event="2" data-index="3">
      <article class="card event-card">
        <a class="link" href="/kultur-und-freizeit/veranstaltungen/eventsingle/event/ganztages-event">
          <div class="card-body">
            <h3 class="card-title">Ganztages-Flohmarkt</h3>
            <div>
              <span class="icon-text"> <span class="icon" aria-hidden="true">event</span> <span> 11.10.2026 </span> </span>
              <span class="icon-text"> <span class="icon" aria-hidden="true">location_on</span> <span> Leuthof </span> </span>
            </div>
          </div>
        </a>
      </article>
    </li>
    """;

    private const string BrokenCardHtml = """
    <li data-pid="9" data-event="10" data-index="11">
      <article class="card event-card">
        <a class="link" href="/kultur-und-freizeit/veranstaltungen/eventsingle/event/ohne-datum">
          <div class="card-body">
            <h3 class="card-title">Eintrag ohne verwertbares Datum</h3>
            <div>
              <span class="icon-text"> <span class="icon" aria-hidden="true">event</span> <span> morgen </span> </span>
              <span class="icon-text"> <span class="icon" aria-hidden="true">location_on</span> <span> Leuthof </span> </span>
            </div>
          </div>
        </a>
      </article>
    </li>
    """;

    private const string ProtocolRelativeCardHtml = """
    <li data-pid="12" data-event="13" data-index="14">
      <article class="card event-card">
        <a class="link" href="//www.leipzig.de/kultur-und-freizeit/veranstaltungen/eventsingle/event/protocol-relativ">
          <div class="card-body">
            <h3 class="card-title">Protocol-relativer Link</h3>
            <div>
              <span class="icon-text"> <span class="icon" aria-hidden="true">event</span> <span> 12.10.2026 </span> </span>
              <span class="icon-text"> <span class="icon" aria-hidden="true">location_on</span> <span> Augustusplatz </span> </span>
            </div>
          </div>
        </a>
      </article>
    </li>
    """;

    [Fact]
    public void Parse_FullCard_ExtractsAllFields()
    {
        var events = WeekendEventParser.Parse(FullCardHtml);

        var weekendEvent = Assert.Single(events);
        Assert.Equal("Wochenmarkt in der Innenstadt", weekendEvent.Title);
        Assert.Equal(new DateTime(2026, 10, 10), weekendEvent.StartDate);
        Assert.Equal(new DateTime(2026, 10, 10), weekendEvent.EndDate);
        Assert.Equal("10:00 – 16:00 Uhr", weekendEvent.TimeText);
        Assert.Equal("Richard-Wagner-Platz", weekendEvent.Location);
        Assert.Equal("Messen", weekendEvent.Topic);
        Assert.Equal(
            "https://www.leipzig.de/kultur-und-freizeit/veranstaltungen/eventsingle/event/wochenmarkt-in-der-innenstadt",
            weekendEvent.Url);
    }

    [Fact]
    public void Parse_DecodesEntities_AndNormalizesTimeWithoutUhr()
    {
        var events = WeekendEventParser.Parse(MinimalCardHtml);

        var weekendEvent = Assert.Single(events);
        Assert.Equal("Sonderführung: Herbst & Geschichte", weekendEvent.Title);
        Assert.Equal("10:00 Uhr", weekendEvent.TimeText);
        Assert.Equal("Zeitgeschichtliches Forum", weekendEvent.Location);
        Assert.Null(weekendEvent.Topic);
    }

    [Fact]
    public void Parse_MultiDayRange_SetsEndDateWithoutTime()
    {
        var events = WeekendEventParser.Parse(MultiDayCardHtml);

        var weekendEvent = Assert.Single(events);
        Assert.Equal(new DateTime(2026, 10, 3), weekendEvent.StartDate);
        Assert.Equal(new DateTime(2026, 10, 4), weekendEvent.EndDate);
        Assert.Null(weekendEvent.TimeText);
    }

    [Fact]
    public void Parse_DateWithoutTime_KeepsDateAndSkipsTime()
    {
        var events = WeekendEventParser.Parse(DateOnlyCardHtml);

        var weekendEvent = Assert.Single(events);
        Assert.Equal(new DateTime(2026, 10, 11), weekendEvent.StartDate);
        Assert.Equal(weekendEvent.StartDate, weekendEvent.EndDate);
        Assert.Null(weekendEvent.TimeText);
    }

    [Fact]
    public void Parse_SkipsCardsWithoutParseableDate()
    {
        var events = WeekendEventParser.Parse(BrokenCardHtml);

        Assert.Empty(events);
    }

    [Fact]
    public void Parse_ProtocolRelativeLink_BecomesHttpsAbsoluteUrl()
    {
        var events = WeekendEventParser.Parse(ProtocolRelativeCardHtml);

        var weekendEvent = Assert.Single(events);
        Assert.Equal("Protocol-relativer Link", weekendEvent.Title);
        Assert.Equal("https://www.leipzig.de/kultur-und-freizeit/veranstaltungen/eventsingle/event/protocol-relativ", weekendEvent.Url);
    }

    [Fact]
    public void Parse_MultipleCards_ReturnsThemInPageOrder()
    {
        var html = FullCardHtml + MinimalCardHtml + BrokenCardHtml + MultiDayCardHtml;

        var events = WeekendEventParser.Parse(html);

        Assert.Equal(3, events.Count);
        Assert.Equal("Wochenmarkt in der Innenstadt", events[0].Title);
        Assert.Equal("Sonderführung: Herbst & Geschichte", events[1].Title);
        Assert.Equal("Europacup im Speedklettern 2026", events[2].Title);
    }

    [Fact]
    public void Parse_EmptyOrMissingHtml_ReturnsEmptyList()
    {
        Assert.Empty(WeekendEventParser.Parse(string.Empty));
        Assert.Empty(WeekendEventParser.Parse("   "));
        Assert.Empty(WeekendEventParser.Parse("<html><body>Keine Termine.</body></html>"));
    }
}
