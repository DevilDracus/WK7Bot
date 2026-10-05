using System.Net;
using System.Text;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using WK7Bot.Services;
using Xunit;

namespace WK7Bot.Tests;

public class LeipzigWasteServiceTests
{
    private const string FeedUrl = "https://example.com/calendar.ics";

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
            => _responder = responder;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(_responder(request));
    }

    private static IConfiguration CreateConfiguration(string? url = FeedUrl)
    {
        var data = new Dictionary<string, string?>();
        if (url != null)
        {
            data["LeipzigWaste:IcsFeedUrl"] = url;
        }

        return new ConfigurationBuilder().AddInMemoryCollection(data).Build();
    }

    private static LeipzigWasteService CreateService(HttpMessageHandler handler, IConfiguration? configuration = null)
    {
        var httpClient = new HttpClient(handler);
        return new LeipzigWasteService(
            httpClient,
            configuration ?? CreateConfiguration(),
            new MemoryCache(new MemoryCacheOptions()),
            NullLogger<LeipzigWasteService>.Instance);
    }

    private static string BuildIcs(params string[] events)
    {
        var body = string.Join("\n", events);
        return $"BEGIN:VCALENDAR\nVERSION:2.0\nPRODID:-//Test//EN\n{body}\nEND:VCALENDAR";
    }

    private static string BuildEvent(string dateIso, string summary)
        => $"BEGIN:VEVENT\nUID:{Guid.NewGuid()}\nDTSTART;VALUE=DATE:{dateIso}\nSUMMARY:{summary}\nEND:VEVENT";

    private static HttpResponseMessage Text(string body, HttpStatusCode status = HttpStatusCode.OK)
        => new(status) { Content = new StringContent(body, Encoding.UTF8, "text/calendar") };

    [Fact]
    public async Task GetWasteTypesForDateAsync_ReturnsEmpty_WhenUrlNotConfigured()
    {
        var service = CreateService(
            new StubHttpMessageHandler(_ => throw new InvalidOperationException("should not be called")),
            CreateConfiguration(url: null));

        var result = await service.GetWasteTypesForDateAsync(new DateTime(2026, 9, 20));

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetWasteTypesForDateAsync_ReturnsEmpty_WhenPayloadNotIcs()
    {
        var service = CreateService(new StubHttpMessageHandler(_ => Text("<html>not ics</html>")));

        var result = await service.GetWasteTypesForDateAsync(new DateTime(2026, 9, 20));

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetWasteTypesForDateAsync_HandlesUtf8BomPrefix()
    {
        var ics = "\uFEFF" + BuildIcs(BuildEvent("20260920", "Restabfall"));
        var service = CreateService(new StubHttpMessageHandler(_ => Text(ics)));

        var result = await service.GetWasteTypesForDateAsync(new DateTime(2026, 9, 20));

        Assert.Single(result);
        Assert.Contains("Schwarze Tonne", result[0]);
    }

    [Fact]
    public async Task GetWasteTypesForDateAsync_HandlesLeadingWhitespaceBeforeVCalendar()
    {
        var ics = "\r\n  " + BuildIcs(BuildEvent("20260920", "Papier"));
        var service = CreateService(new StubHttpMessageHandler(_ => Text(ics)));

        var result = await service.GetWasteTypesForDateAsync(new DateTime(2026, 9, 20));

        Assert.Single(result);
        Assert.Contains("Blaue Tonne", result[0]);
    }

    [Fact]
    public async Task GetWasteTypesForDateAsync_ReturnsEmpty_WhenHttpFails()
    {
        var service = CreateService(new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.InternalServerError)));

        var result = await service.GetWasteTypesForDateAsync(new DateTime(2026, 9, 20));

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetWasteTypesForDateAsync_IgnoresEventsOnOtherDates()
    {
        var ics = BuildIcs(
            BuildEvent("20260921", "Restabfall"),
            BuildEvent("20260922", "Papier"));

        var service = CreateService(new StubHttpMessageHandler(_ => Text(ics)));

        var result = await service.GetWasteTypesForDateAsync(new DateTime(2026, 9, 20));

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetWasteTypesForDateAsync_MapsAllKnownWasteCategories()
    {
        var ics = BuildIcs(
            BuildEvent("20260920", "Restabfall"),
            BuildEvent("20260920", "Papier"),
            BuildEvent("20260920", "Wertstoffe"),
            BuildEvent("20260920", "Biogut"));

        var service = CreateService(new StubHttpMessageHandler(_ => Text(ics)));

        var result = await service.GetWasteTypesForDateAsync(new DateTime(2026, 9, 20));

        Assert.Equal(4, result.Count);
        Assert.Contains(result, r => r.Contains("Schwarze Tonne"));
        Assert.Contains(result, r => r.Contains("Blaue Tonne"));
        Assert.Contains(result, r => r.Contains("Gelbe Tonne"));
        Assert.Contains(result, r => r.Contains("Braune Tonne"));
    }

    [Fact]
    public async Task GetWasteTypesForDateAsync_MapsColorAliases()
    {
        var ics = BuildIcs(
            BuildEvent("20260920", "schwarz"),
            BuildEvent("20260920", "blau"),
            BuildEvent("20260920", "gelb"),
            BuildEvent("20260920", "braun"));

        var service = CreateService(new StubHttpMessageHandler(_ => Text(ics)));

        var result = await service.GetWasteTypesForDateAsync(new DateTime(2026, 9, 20));

        Assert.Equal(4, result.Count);
        Assert.Contains(result, r => r.Contains("Schwarze Tonne"));
        Assert.Contains(result, r => r.Contains("Blaue Tonne"));
        Assert.Contains(result, r => r.Contains("Gelbe Tonne"));
        Assert.Contains(result, r => r.Contains("Braune Tonne"));
    }

    [Fact]
    public async Task GetWasteTypesForDateAsync_FallsBackToTrashEmoji_ForUnknownSummary()
    {
        var ics = BuildIcs(BuildEvent("20260920", "Sperrmüll Sondertermin"));
        var service = CreateService(new StubHttpMessageHandler(_ => Text(ics)));

        var result = await service.GetWasteTypesForDateAsync(new DateTime(2026, 9, 20));

        Assert.Single(result);
        Assert.StartsWith("🗑️", result[0]);
        Assert.Contains("Sperrmüll", result[0]);
    }

    [Fact]
    public async Task GetWasteTypesForDateAsync_ReturnsEmpty_WhenCalendarHasNoEvents()
    {
        var service = CreateService(new StubHttpMessageHandler(_ => Text(BuildIcs())));

        var result = await service.GetWasteTypesForDateAsync(new DateTime(2026, 9, 20));

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetWasteTypesForDateAsync_DownloadsFeedOnlyOnce_AcrossCalls()
    {
        var calls = 0;
        var ics = BuildIcs(BuildEvent("20260920", "Restabfall"));
        var service = CreateService(new StubHttpMessageHandler(_ =>
        {
            calls++;
            return Text(ics);
        }));

        var first = await service.GetWasteTypesForDateAsync(new DateTime(2026, 9, 20));
        var second = await service.GetWasteTypesForDateAsync(new DateTime(2026, 9, 20));
        var otherDate = await service.GetWasteTypesForDateAsync(new DateTime(2026, 9, 21));

        Assert.Equal(1, calls);
        Assert.Single(first);
        Assert.Single(second);
        Assert.Empty(otherDate);
    }

    [Fact]
    public async Task GetWasteTypesForDateAsync_RetriesDownload_AfterFailure()
    {
        var calls = 0;
        var ics = BuildIcs(BuildEvent("20260920", "Papier"));
        var service = CreateService(new StubHttpMessageHandler(_ =>
        {
            calls++;
            return calls == 1
                ? new HttpResponseMessage(HttpStatusCode.InternalServerError)
                : Text(ics);
        }));

        var failed = await service.GetWasteTypesForDateAsync(new DateTime(2026, 9, 20));
        var retried = await service.GetWasteTypesForDateAsync(new DateTime(2026, 9, 20));

        Assert.Equal(2, calls);
        Assert.Empty(failed);
        Assert.Single(retried);
    }

    [Fact]
    public async Task GetWasteTypesForDateAsync_DoesNotCache_InvalidPayload()
    {
        var calls = 0;
        var ics = BuildIcs(BuildEvent("20260920", "Papier"));
        var service = CreateService(new StubHttpMessageHandler(_ =>
        {
            calls++;
            return calls == 1
                ? Text("<html>not ics</html>")
                : Text(ics);
        }));

        var invalid = await service.GetWasteTypesForDateAsync(new DateTime(2026, 9, 20));
        var valid = await service.GetWasteTypesForDateAsync(new DateTime(2026, 9, 20));

        Assert.Equal(2, calls);
        Assert.Empty(invalid);
        Assert.Single(valid);
    }
}
