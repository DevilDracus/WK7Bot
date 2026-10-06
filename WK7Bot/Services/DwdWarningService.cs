namespace WK7Bot.Services;

using System.IO.Compression;
using System.Text.RegularExpressions;
using WK7Bot.Core.Utilities;
using WK7Bot.Models;
using WK7Bot.Services.Interfaces;

/// <summary>
/// Fetches official weather warnings from the DWD open-data CAP feed: reads the directory listing,
/// selects the newest German snapshot archive, and parses every alert it contains. An empty
/// snapshot (DWD publishes a 22-byte end-of-central-directory zip when no warning is active)
/// yields an empty list instead of an error.
/// </summary>
public partial class DwdWarningService : IDwdWarningService
{
    /// <summary>
    /// Base URL of the DWD commune-union warning feed directory.
    /// </summary>
    public const string ListingUrl = "https://opendata.dwd.de/weather/alerts/cap/COMMUNEUNION_DWD_STAT/";

    /// <summary>
    /// Size in bytes of the empty snapshot archive DWD publishes when no warning is active.
    /// </summary>
    public const int EmptySnapshotLength = 22;

    private readonly HttpClient _httpClient;
    private readonly ILogger<DwdWarningService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="DwdWarningService"/> class.
    /// </summary>
    /// <param name="httpClient">The HTTP client used for feed requests.</param>
    /// <param name="logger">The logger instance for feed diagnostics.</param>
    public DwdWarningService(HttpClient httpClient, ILogger<DwdWarningService> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Matches the German snapshot archives in the feed directory listing
    /// (<c>Z_CAP_C_EDZW_&lt;yyyyMMddHHmmss&gt;_PVW_STATUS_PREMIUMDWD_COMMUNEUNION_de.zip</c>).
    /// </summary>
    [GeneratedRegex(@"Z_CAP_C_EDZW_(\d{14})_PVW_STATUS_PREMIUMDWD_COMMUNEUNION_de\.zip", RegexOptions.CultureInvariant)]
    private static partial Regex SnapshotRegex();

    /// <summary>
    /// Fetches the newest DWD warning snapshot and returns every parsed alert it contains.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for network operations.</param>
    /// <returns>The warnings of the newest snapshot; empty when the snapshot carries none.</returns>
    /// <exception cref="HttpRequestException">The listing or the snapshot could not be downloaded.</exception>
    /// <exception cref="InvalidOperationException">The listing contains no German snapshot archive.</exception>
    public async Task<IReadOnlyList<CapWarning>> FetchWarningsAsync(CancellationToken cancellationToken = default)
    {
        var listing = await _httpClient.GetStringAsync(ListingUrl, cancellationToken);
        var snapshotName = SelectNewestSnapshot(listing);

        if (snapshotName is null)
        {
            throw new InvalidOperationException($"No German warning snapshot found at {ListingUrl}.");
        }

        var archiveBytes = await _httpClient.GetByteArrayAsync(ListingUrl + snapshotName, cancellationToken);

        if (archiveBytes.Length <= EmptySnapshotLength)
        {
            _logger.LogDebug("DWD warning snapshot {Snapshot} is empty (no active warnings).", snapshotName);
            return Array.Empty<CapWarning>();
        }

        return ParseSnapshot(archiveBytes, snapshotName);
    }

    /// <summary>
    /// Picks the German snapshot with the newest embedded timestamp from a directory listing.
    /// </summary>
    /// <param name="listingHtml">The raw directory listing HTML.</param>
    /// <returns>The file name of the newest snapshot, or <see langword="null"/> when none matches.</returns>
    public static string? SelectNewestSnapshot(string listingHtml)
    {
        if (string.IsNullOrEmpty(listingHtml))
        {
            return null;
        }

        string? newestName = null;
        var newestStamp = string.Empty;

        foreach (Match match in SnapshotRegex().Matches(listingHtml))
        {
            var stamp = match.Groups[1].Value;
            if (string.CompareOrdinal(stamp, newestStamp) > 0)
            {
                newestStamp = stamp;
                newestName = match.Value;
            }
        }

        return newestName;
    }

    /// <summary>
    /// Opens a snapshot zip archive in memory and parses every CAP alert it contains. Entries that
    /// cannot be parsed are logged and skipped so one broken warning does not hide the others.
    /// </summary>
    /// <param name="archiveBytes">The zip archive bytes.</param>
    /// <param name="snapshotName">The snapshot file name (for log context).</param>
    /// <returns>The parsed warnings.</returns>
    private IReadOnlyList<CapWarning> ParseSnapshot(byte[] archiveBytes, string snapshotName)
    {
        var warnings = new List<CapWarning>();

        using var archive = new ZipArchive(new MemoryStream(archiveBytes), ZipArchiveMode.Read);

        foreach (var entry in archive.Entries)
        {
            if (!entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                using var stream = entry.Open();
                warnings.AddRange(CapWarningParser.ParseAlert(stream));
            }
            catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
            {
                _logger.LogWarning(ex, "Skipping malformed CAP entry {Entry} in DWD snapshot {Snapshot}.", entry.FullName, snapshotName);
            }
        }

        _logger.LogDebug("Parsed {Count} warning(s) from DWD snapshot {Snapshot}.", warnings.Count, snapshotName);
        return warnings;
    }
}
