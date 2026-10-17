using System.Net;
using System.Text;
using System.Xml.Linq;
using Microsoft.Extensions.Logging.Abstractions;
using WK7Bot.Core.Entities;
using WK7Bot.Services;
using Xunit;

namespace WK7Bot.Tests;

public class OpmlServiceTests
{
    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
            => _responder = responder;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(_responder(request));
    }

    private static OpmlService CreateService(Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        var httpClient = new HttpClient(new StubHttpMessageHandler(responder));
        return new OpmlService(httpClient, NullLogger<OpmlService>.Instance);
    }

    [Fact]
    public void BuildExport_ProducesOpmlWithEveryFeed()
    {
        var service = CreateService(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var feeds = new List<RssFeed>
        {
            new() { Name = "PD Leipzig", Url = "https://example.com/pd.xml" },
            new() { Name = "Tagesschau", Url = "https://example.com/ts.xml" }
        };

        var opml = service.BuildExport(feeds);

        var document = XDocument.Parse(opml);
        Assert.Equal("opml", document.Root!.Name.LocalName);
        var outlines = document.Descendants("outline").ToList();
        Assert.Equal(2, outlines.Count);
        Assert.Equal("https://example.com/pd.xml", (string?)outlines[0].Attribute("xmlUrl"));
        Assert.Equal("Tagesschau", (string?)outlines[1].Attribute("text"));
    }

    [Fact]
    public async Task FetchAsync_ParsesOutlinesAndDeduplicatesByUrl()
    {
        const string opml = """
            <?xml version="1.0" encoding="utf-8"?>
            <opml version="2.0">
              <head><title>Test</title></head>
              <body>
                <outline text="A" xmlUrl="https://example.com/a.xml" />
                <outline text="A duplicate" xmlUrl="https://example.com/a.xml" />
                <outline text="B" xmlUrl="https://example.com/b.xml" />
                <outline text="Group">
                  <outline title="C" xmlUrl="https://example.com/c.xml" />
                </outline>
              </body>
            </opml>
            """;

        var service = CreateService(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(opml, Encoding.UTF8, "text/xml")
        });

        var entries = await service.FetchAsync("https://example.com/feeds.opml");

        Assert.Equal(3, entries.Count);
        Assert.Equal("https://example.com/a.xml", entries[0].Url);
        Assert.Equal("C", entries[2].Title);
    }

    [Fact]
    public async Task FetchAsync_UsesUrlAsTitleFallback()
    {
        const string opml = """
            <?xml version="1.0" encoding="utf-8"?>
            <opml version="2.0"><body>
              <outline xmlUrl="https://example.com/a.xml" />
              <outline xmlUrl="https://example.com/b.xml" />
            </body></opml>
            """;

        var service = CreateService(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(opml, Encoding.UTF8, "text/xml")
        });

        var entries = await service.FetchAsync("https://example.com/feeds.opml");

        Assert.Equal("https://example.com/a.xml", entries[0].Title);
    }

    [Fact]
    public async Task FetchAsync_RejectsNonHttpUrl()
    {
        var service = CreateService(_ => new HttpResponseMessage(HttpStatusCode.OK));

        var entries = await service.FetchAsync("file:///etc/passwd");

        Assert.Empty(entries);
    }

    [Fact]
    public async Task FetchAsync_ReturnsEmpty_WhenDocumentIsNotXml()
    {
        var service = CreateService(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("not xml at all", Encoding.UTF8, "text/plain")
        });

        var entries = await service.FetchAsync("https://example.com/broken.opml");

        Assert.Empty(entries);
    }
}
