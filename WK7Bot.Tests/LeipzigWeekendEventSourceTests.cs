using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using WK7Bot.Services;
using Xunit;

namespace WK7Bot.Tests;

public class LeipzigWeekendEventSourceTests
{
    private static readonly DateTime WeekendFriday = new(2026, 10, 9);

    private const string SaturdayCardHtml = """
    <li data-pid="1" data-event="100" data-index="1">
      <article class="card event-card">
        <a class="link" href="/kultur-und-freizeit/veranstaltungen/eventsingle/event/wochenmarkt-samstag">
          <div class="card-body">
            <h3 class="card-title">Wochenmarkt Samstag</h3>
            <div>
              <span class="icon-text"> <span class="icon" aria-hidden="true">event</span> <span> 10.10.2026 &middot; 10:00 &ndash; 16:00 Uhr </span> </span>
              <span class="icon-text"> <span class="icon" aria-hidden="true">location_on</span> <span> Richard-Wagner-Platz </span> </span>
            </div>
          </div>
        </a>
      </article>
    </li>
    """;

    private const string FridayCardHtml = """
    <li data-pid="2" data-event="101" data-index="2">
      <article class="card event-card">
        <a class="link" href="/kultur-und-freizeit/veranstaltungen/eventsingle/event/wochenmarkt-freitag">
          <div class="card-body">
            <h3 class="card-title">Wochenmarkt Freitag</h3>
            <div>
              <span class="icon-text"> <span class="icon" aria-hidden="true">event</span> <span> 09.10.2026 &middot; 09:00 &ndash; 17:40 Uhr </span> </span>
              <span class="icon-text"> <span class="icon" aria-hidden="true">location_on</span> <span> Augustusplatz </span> </span>
            </div>
          </div>
        </a>
      </article>
    </li>
    """;

    private const string OutOfWeekendCardHtml = """
    <li data-pid="3" data-event="102" data-index="3">
      <article class="card event-card">
        <a class="link" href="/kultur-und-freizeit/veranstaltungen/eventsingle/event/markt-vorherige-woche">
          <div class="card-body">
            <h3 class="card-title">Markt der Vorwoche</h3>
            <div>
              <span class="icon-text"> <span class="icon" aria-hidden="true">event</span> <span> 02.10.2026 &middot; 10:00 Uhr </span> </span>
            </div>
          </div>
        </a>
      </article>
    </li>
    """;

    private const string LaterWeekendCardHtml = """
    <li data-pid="4" data-event="103" data-index="4">
      <article class="card event-card">
        <a class="link" href="/kultur-und-freizeit/veranstaltungen/eventsingle/event/markt-naechstes-wochenende">
          <div class="card-body">
            <h3 class="card-title">Markt des nächsten Wochenendes</h3>
            <div>
              <span class="icon-text"> <span class="icon" aria-hidden="true">event</span> <span> 17.10.2026 &middot; 10:00 Uhr </span> </span>
            </div>
          </div>
        </a>
      </article>
    </li>
    """;

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
            => _responder = responder;

        public List<string> RequestUrls { get; } = new();
        public List<string> UserAgents { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RequestUrls.Add(request.RequestUri!.ToString());
            UserAgents.Add(request.Headers.UserAgent.ToString());
            return Task.FromResult(_responder(request));
        }
    }

    private static HttpResponseMessage Html(string html)
        => new(HttpStatusCode.OK)
        {
            Content = new StringContent(html, Encoding.UTF8, "text/html")
        };

    private static LeipzigWeekendEventSource CreateSource(StubHttpMessageHandler handler)
        => new(new HttpClient(handler), NullLogger<LeipzigWeekendEventSource>.Instance);

    [Theory]
    [InlineData(2026, 10, 9, "oktober")]
    [InlineData(2027, 3, 5, "maerz")]
    [InlineData(2026, 12, 31, "dezember")]
    [InlineData(2026, 1, 2, "januar")]
    public void BuildMonthUrl_UsesAsciiGermanMonthSlug(int year, int month, int day, string expectedSlug)
    {
        var url = LeipzigWeekendEventSource.BuildMonthUrl(new DateTime(year, month, day));

        Assert.EndsWith($"/{expectedSlug}", url);
        Assert.StartsWith("https://www.leipzig.de/kultur-und-freizeit/veranstaltungen/", url);
    }

    [Fact]
    public async Task FetchWeekendEventsAsync_MergesBothPages_AndDeduplicatesByUrl()
    {
        var handler = new StubHttpMessageHandler(request =>
            request.RequestUri!.ToString().Contains("termine-dieses-wochenende")
                ? Html(SaturdayCardHtml)
                : Html(SaturdayCardHtml + FridayCardHtml + OutOfWeekendCardHtml + LaterWeekendCardHtml));

        var source = CreateSource(handler);
        var events = await source.FetchWeekendEventsAsync(WeekendFriday);

        Assert.Equal(2, events.Count);
        Assert.Equal(new DateTime(2026, 10, 9), events[0].StartDate);
        Assert.Equal("Wochenmarkt Freitag", events[0].Title);
        Assert.Equal(new DateTime(2026, 10, 10), events[1].StartDate);
        Assert.Equal("Wochenmarkt Samstag", events[1].Title);
    }

    [Fact]
    public async Task FetchWeekendEventsAsync_RequestsBothListingPages_WithBrowserUserAgent()
    {
        var handler = new StubHttpMessageHandler(_ => Html(SaturdayCardHtml));

        var source = CreateSource(handler);
        await source.FetchWeekendEventsAsync(WeekendFriday);

        Assert.Equal(2, handler.RequestUrls.Count);
        Assert.Contains(handler.RequestUrls, u => u.Contains("termine-dieses-wochenende"));
        Assert.Contains(handler.RequestUrls, u => u.EndsWith("/oktober"));
        Assert.All(handler.UserAgents, ua => Assert.Contains("Mozilla/5.0", ua));
    }

    [Fact]
    public async Task FetchWeekendEventsAsync_SurvivesSinglePageFailure()
    {
        var handler = new StubHttpMessageHandler(request =>
            request.RequestUri!.ToString().Contains("termine-dieses-wochenende")
                ? new HttpResponseMessage(HttpStatusCode.InternalServerError)
                : Html(FridayCardHtml));

        var source = CreateSource(handler);
        var events = await source.FetchWeekendEventsAsync(WeekendFriday);

        var weekendEvent = Assert.Single(events);
        Assert.Equal("Wochenmarkt Freitag", weekendEvent.Title);
    }

    [Fact]
    public async Task FetchWeekendEventsAsync_ThrowsWhenAllPagesFail()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));

        var source = CreateSource(handler);
        await Assert.ThrowsAsync<HttpRequestException>(() => source.FetchWeekendEventsAsync(WeekendFriday));
    }

    [Fact]
    public async Task FetchWeekendEventsAsync_ReturnsEmptyList_WhenWeekendHasNoEvents()
    {
        var handler = new StubHttpMessageHandler(_ => Html(OutOfWeekendCardHtml + LaterWeekendCardHtml));

        var source = CreateSource(handler);
        var events = await source.FetchWeekendEventsAsync(WeekendFriday);

        Assert.Empty(events);
    }
}
