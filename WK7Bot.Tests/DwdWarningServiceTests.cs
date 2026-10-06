using System.IO.Compression;
using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using WK7Bot.Services;
using Xunit;

namespace WK7Bot.Tests;

public class DwdWarningServiceTests
{
    private const string NewerDeSnapshot = "Z_CAP_C_EDZW_20261006131515_PVW_STATUS_PREMIUMDWD_COMMUNEUNION_de.zip";
    private const string OlderDeSnapshot = "Z_CAP_C_EDZW_20261006125445_PVW_STATUS_PREMIUMDWD_COMMUNEUNION_de.zip";
    private const string NewerEnSnapshot = "Z_CAP_C_EDZW_20261006133615_PVW_STATUS_PREMIUMDWD_COMMUNEUNION_en.zip";

    private const string SampleAlert =
        """
        <alert xmlns="urn:oasis:names:tc:emergency:cap:1.2">
          <identifier>warn-1</identifier>
          <sent>2026-10-06T13:15:00+02:00</sent>
          <info>
            <event>STARKREGEN</event>
            <severity>Severe</severity>
            <onset>2026-10-06T14:00:00+02:00</onset>
            <expires>2026-10-06T16:00:00+02:00</expires>
            <headline>Amtliche WARNUNG vor STARKREGEN</headline>
          </info>
        </alert>
        """;

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
            => _responder = responder;

        public List<string> RequestUrls { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RequestUrls.Add(request.RequestUri!.ToString());
            return Task.FromResult(_responder(request));
        }
    }

    private static HttpResponseMessage Html(string html)
        => new(HttpStatusCode.OK)
        {
            Content = new StringContent(html, Encoding.UTF8, "text/html")
        };

    private static HttpResponseMessage Zip(byte[] content)
        => new(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(content)
        };

    private static byte[] BuildZip(params (string Name, string Content)[] entries)
    {
        using var stream = new MemoryStream();

        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in entries)
            {
                var entry = archive.CreateEntry(name);
                using var writer = new StreamWriter(entry.Open());
                writer.Write(content);
            }
        }

        return stream.ToArray();
    }

    private static string Listing(params string[] names)
    {
        var rows = string.Join(
            '\n',
            names.Select(name => $"<a href=\"{name}\">{name}</a>  06-Oct-2026 13:15:15  12.3K"));

        return $"""
            <html><head><title>Index of /weather/alerts/cap/COMMUNEUNION_DWD_STAT/</title></head>
            <body><h1>Index of /weather/alerts/cap/COMMUNEUNION_DWD_STAT/</h1><table>
            {rows}
            </table></body></html>
            """;
    }

    private static DwdWarningService CreateService(StubHttpMessageHandler handler)
        => new(new HttpClient(handler), NullLogger<DwdWarningService>.Instance);

    [Fact]
    public async Task FetchWarningsAsync_SelectsNewestGermanSnapshot_AndParsesItsAlerts()
    {
        var handler = new StubHttpMessageHandler(request =>
            request.RequestUri!.ToString().EndsWith('/')
                ? Html(Listing(OlderDeSnapshot, NewerDeSnapshot, NewerEnSnapshot, "FTP_ftp.zip", "README.txt"))
                : Zip(BuildZip(("warn.xml", SampleAlert))));

        var service = CreateService(handler);
        var warnings = await service.FetchWarningsAsync();

        var warning = Assert.Single(warnings);
        Assert.Equal("warn-1", warning.Identifier);
        Assert.Equal("STARKREGEN", warning.Event);
        Assert.Equal(2, handler.RequestUrls.Count);
        Assert.EndsWith("/COMMUNEUNION_DWD_STAT/", handler.RequestUrls[0]);
        Assert.EndsWith(NewerDeSnapshot, handler.RequestUrls[1]);
    }

    [Fact]
    public async Task FetchWarningsAsync_EmptySnapshot_ReturnsEmptyList()
    {
        var handler = new StubHttpMessageHandler(request =>
            request.RequestUri!.ToString().EndsWith('/')
                ? Html(Listing(NewerDeSnapshot))
                : Zip(BuildZip()));

        var service = CreateService(handler);
        var warnings = await service.FetchWarningsAsync();

        Assert.Empty(warnings);
        Assert.Equal(DwdWarningService.EmptySnapshotLength, BuildZip().Length);
    }

    [Fact]
    public async Task FetchWarningsAsync_SkipsMalformedXmlEntries()
    {
        var handler = new StubHttpMessageHandler(request =>
            request.RequestUri!.ToString().EndsWith('/')
                ? Html(Listing(NewerDeSnapshot))
                : Zip(BuildZip(("broken.xml", "<alert><info></alert>"), ("good.xml", SampleAlert))));

        var service = CreateService(handler);
        var warnings = await service.FetchWarningsAsync();

        Assert.Single(warnings);
    }

    [Fact]
    public async Task FetchWarningsAsync_NoSnapshotInListing_Throws()
    {
        var handler = new StubHttpMessageHandler(_ => Html(Listing(NewerEnSnapshot, "README.txt")));

        var service = CreateService(handler);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.FetchWarningsAsync());
    }

    [Fact]
    public async Task FetchWarningsAsync_ListingHttpFailure_Throws()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));

        var service = CreateService(handler);

        await Assert.ThrowsAsync<HttpRequestException>(() => service.FetchWarningsAsync());
    }

    [Fact]
    public void SelectNewestSnapshot_IgnoresOtherLanguagesAndReturnsNullWhenAbsent()
    {
        Assert.Null(DwdWarningService.SelectNewestSnapshot(string.Empty));
        Assert.Null(DwdWarningService.SelectNewestSnapshot(Listing(NewerEnSnapshot)));
        Assert.Equal(NewerDeSnapshot, DwdWarningService.SelectNewestSnapshot(Listing(OlderDeSnapshot, NewerDeSnapshot)));
    }
}
