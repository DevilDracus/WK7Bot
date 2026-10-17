namespace WK7Bot.Services;

using System.Text;
using System.Xml;
using System.Xml.Linq;
using WK7Bot.Core.Entities;
using WK7Bot.Services.Interfaces;

/// <summary>
/// Default <see cref="IOpmlService"/> implementation: OPML 2.0 export via <see cref="XDocument"/> and
/// import by downloading the document over HTTP and reading every <c>outline</c> element with an
/// <c>xmlUrl</c> attribute.
/// </summary>
public class OpmlService : IOpmlService
{
    /// <summary>Upper bound on imported entries so a huge OPML file cannot create hundreds of channels.</summary>
    public const int MaxImportEntries = 15;

    private readonly HttpClient _httpClient;
    private readonly ILogger<OpmlService> _logger;

    /// <summary>Initializes a new instance of the <see cref="OpmlService"/> class.</summary>
    /// <param name="httpClient">The HTTP client used to download OPML documents.</param>
    /// <param name="logger">The logger instance.</param>
    public OpmlService(HttpClient httpClient, ILogger<OpmlService> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public string BuildExport(IReadOnlyList<RssFeed> feeds)
    {
        ArgumentNullException.ThrowIfNull(feeds);

        var document = new XDocument(
            new XDeclaration("1.0", "utf-8", null),
            new XElement("opml",
                new XAttribute("version", "2.0"),
                new XElement("head",
                    new XElement("title", "WK7 Bot RSS Feeds")),
                new XElement("body",
                    feeds.Select(feed => new XElement("outline",
                        new XAttribute("text", feed.Name),
                        new XAttribute("title", feed.Name),
                        new XAttribute("type", "rss"),
                        new XAttribute("xmlUrl", feed.Url))))));

        var builder = new StringBuilder();
        using (var writer = XmlWriter.Create(builder, new XmlWriterSettings
               {
                   Indent = true,
                   Encoding = new UTF8Encoding(false)
               }))
        {
            document.Save(writer);
        }

        return builder.ToString();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<OpmlFeedEntry>> FetchAsync(string url, CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return Array.Empty<OpmlFeedEntry>();
        }

        try
        {
            var xml = await _httpClient.GetStringAsync(uri, cancellationToken).ConfigureAwait(false);
            var document = XDocument.Parse(xml);
            var entries = new List<OpmlFeedEntry>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var outline in document.Descendants("outline"))
            {
                var xmlUrl = (string?)outline.Attribute("xmlUrl")
                             ?? (string?)outline.Attribute("xmlurl");
                if (string.IsNullOrWhiteSpace(xmlUrl))
                {
                    continue;
                }

                var trimmedUrl = xmlUrl.Trim();
                if (!seen.Add(trimmedUrl))
                {
                    continue;
                }

                var title = (string?)outline.Attribute("title")
                            ?? (string?)outline.Attribute("text")
                            ?? trimmedUrl;

                entries.Add(new OpmlFeedEntry(title.Trim(), trimmedUrl));

                if (entries.Count >= MaxImportEntries)
                {
                    break;
                }
            }

            return entries;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Could not download or parse the OPML document from '{Url}'.", url);
            return Array.Empty<OpmlFeedEntry>();
        }
    }
}
