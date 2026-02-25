// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

namespace Sharc.Arc.Locators;

/// <summary>
/// Resolves arc files from HTTP/HTTPS URLs. Supports direct links, CDN URLs,
/// and cloud storage shared links (Dropbox, Google Drive, S3 presigned URLs).
/// <para>
/// Downloads the arc file to a temporary location, validates it, then opens it.
/// Never throws — all errors returned as <see cref="ArcOpenResult"/>.
/// </para>
/// <para>
/// <b>Cloud storage support:</b>
/// <list type="bullet">
///   <item><b>Dropbox</b>: Converts <c>?dl=0</c> to <c>?dl=1</c> for direct download</item>
///   <item><b>Google Drive</b>: Converts sharing URLs to direct download format</item>
///   <item><b>S3/Azure/GCS</b>: Presigned URLs work as-is (direct HTTP download)</item>
///   <item><b>Any HTTP</b>: Direct download of .sharc/.arc files</item>
/// </list>
/// </para>
/// </summary>
public sealed class HttpArcLocator : IArcLocator, IDisposable
{
    // SQLite format 3 magic bytes
    private static readonly byte[] SqliteMagic = "SQLite format 3\0"u8.ToArray();

    private readonly HttpClient _httpClient;
    private readonly bool _ownsClient;
    private readonly string _tempDirectory;

    /// <summary>
    /// Creates an HTTP arc locator with a default <see cref="HttpClient"/>.
    /// Downloaded arcs are stored in the system temp directory.
    /// </summary>
    public HttpArcLocator()
        : this(new HttpClient(), ownsClient: true, tempDirectory: null)
    {
    }

    /// <summary>
    /// Creates an HTTP arc locator with a custom <see cref="HttpClient"/>
    /// and optional temp directory for downloaded arcs.
    /// </summary>
    /// <param name="httpClient">The HTTP client to use for downloads.</param>
    /// <param name="ownsClient">Whether this locator owns (and should dispose) the HTTP client.</param>
    /// <param name="tempDirectory">Directory for downloaded arc files. Null = system temp.</param>
    public HttpArcLocator(HttpClient httpClient, bool ownsClient = false, string? tempDirectory = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _ownsClient = ownsClient;
        _tempDirectory = tempDirectory ?? Path.GetTempPath();
        Directory.CreateDirectory(_tempDirectory);
    }

    /// <inheritdoc />
    public string Authority => "https";

    /// <inheritdoc />
    public ArcOpenResult TryOpen(ArcUri uri, ArcOpenOptions? options = null)
    {
        try
        {
            return TryOpenCore(uri, options ?? new ArcOpenOptions());
        }
        catch (Exception ex)
        {
            return ArcOpenResult.Failure(ArcAvailability.Unreachable,
                $"Unexpected error opening remote arc: {ex.Message}");
        }
    }

    private ArcOpenResult TryOpenCore(ArcUri uri, ArcOpenOptions options)
    {
        // 1. Build the download URL from the ArcUri
        string downloadUrl = ResolveDownloadUrl(uri);

        // 2. Download to a temp file
        string tempPath = Path.Combine(_tempDirectory, $"arc_{Guid.NewGuid():N}.sharc");

        try
        {
            // Synchronous download (IArcLocator contract is synchronous)
            using var response = _httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead)
                .GetAwaiter().GetResult();

            if (!response.IsSuccessStatusCode)
            {
                return ArcOpenResult.Failure(ArcAvailability.Unreachable,
                    $"HTTP {(int)response.StatusCode} ({response.ReasonPhrase}) downloading '{downloadUrl}'.");
            }

            // Check content length against size limit before downloading body
            if (response.Content.Headers.ContentLength is long contentLength
                && contentLength > options.MaxFileSizeBytes)
            {
                return ArcOpenResult.Failure(ArcAvailability.Untrusted,
                    $"Remote arc exceeds size limit: {contentLength:N0} bytes > {options.MaxFileSizeBytes:N0} bytes.");
            }

            // Stream to temp file with size enforcement
            using (var stream = response.Content.ReadAsStream())
            using (var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                long totalBytes = 0;
                var buffer = new byte[81920]; // 80 KB buffer
                int bytesRead;

                while ((bytesRead = stream.Read(buffer, 0, buffer.Length)) > 0)
                {
                    totalBytes += bytesRead;
                    if (totalBytes > options.MaxFileSizeBytes)
                    {
                        fileStream.Close();
                        CleanupTempFile(tempPath);
                        return ArcOpenResult.Failure(ArcAvailability.Untrusted,
                            $"Remote arc exceeds size limit during download: > {options.MaxFileSizeBytes:N0} bytes.");
                    }
                    fileStream.Write(buffer, 0, bytesRead);
                }
            }
        }
        catch (HttpRequestException ex)
        {
            CleanupTempFile(tempPath);
            return ArcOpenResult.Failure(ArcAvailability.Unreachable,
                $"Network error downloading arc: {ex.Message}");
        }
        catch (TaskCanceledException)
        {
            CleanupTempFile(tempPath);
            return ArcOpenResult.Failure(ArcAvailability.Unreachable,
                "Download timed out.");
        }

        // 3. Validate SQLite magic
        Span<byte> header = stackalloc byte[16];
        using (var fs = new FileStream(tempPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            if (fs.Read(header) < 16)
            {
                CleanupTempFile(tempPath);
                return ArcOpenResult.Failure(ArcAvailability.Untrusted,
                    "Downloaded file too small to be a valid database.");
            }
        }

        if (!header.SequenceEqual(SqliteMagic))
        {
            CleanupTempFile(tempPath);
            return ArcOpenResult.Failure(ArcAvailability.Untrusted,
                "Downloaded file is not a valid arc: SQLite magic header mismatch.");
        }

        // 4. Open the database via LocalArcLocator (reuse its validation logic)
        var localUri = ArcUri.Parse($"arc://local/{tempPath}");
        var localOptions = new ArcOpenOptions
        {
            MaxFileSizeBytes = options.MaxFileSizeBytes,
            ValidateOnOpen = options.ValidateOnOpen,
            TrustAnchors = options.TrustAnchors,
            UnknownSignerPolicy = options.UnknownSignerPolicy
        };

        var localLocator = new LocalArcLocator();
        return localLocator.TryOpen(localUri, localOptions);
    }

    /// <summary>
    /// Resolves cloud storage sharing URLs to direct download URLs.
    /// </summary>
    internal static string ResolveDownloadUrl(ArcUri uri)
    {
        string url = uri.Path;

        // If the path is already a full URL, use it directly
        if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return TransformCloudUrl(url);
        }

        // Build URL from authority + path
        return TransformCloudUrl($"https://{uri.Authority}/{url}");
    }

    /// <summary>
    /// Transforms cloud storage sharing URLs to direct download URLs.
    /// </summary>
    internal static string TransformCloudUrl(string url)
    {
        // ── Dropbox ──
        // Share link: https://www.dropbox.com/s/xxxxx/file.sharc?dl=0
        // Direct:     https://www.dropbox.com/s/xxxxx/file.sharc?dl=1
        if (url.Contains("dropbox.com/", StringComparison.OrdinalIgnoreCase))
        {
            if (url.Contains("?dl=0", StringComparison.OrdinalIgnoreCase))
                return url.Replace("?dl=0", "?dl=1");
            if (!url.Contains("?dl=", StringComparison.OrdinalIgnoreCase) &&
                !url.Contains("&dl=", StringComparison.OrdinalIgnoreCase))
                return url + (url.Contains('?') ? "&dl=1" : "?dl=1");
            return url;
        }

        // ── Google Drive ──
        // Share link: https://drive.google.com/file/d/{fileId}/view?usp=sharing
        // Direct:     https://drive.google.com/uc?export=download&id={fileId}
        if (url.Contains("drive.google.com/file/d/", StringComparison.OrdinalIgnoreCase))
        {
            int idStart = url.IndexOf("/d/", StringComparison.OrdinalIgnoreCase) + 3;
            int idEnd = url.IndexOf('/', idStart);
            if (idEnd < 0) idEnd = url.IndexOf('?', idStart);
            if (idEnd < 0) idEnd = url.Length;

            string fileId = url[idStart..idEnd];
            return $"https://drive.google.com/uc?export=download&id={fileId}";
        }

        // ── Google Drive (already uc format) ──
        if (url.Contains("drive.google.com/uc", StringComparison.OrdinalIgnoreCase))
            return url;

        // ── OneDrive ──
        // Share link: https://1drv.ms/u/s!xxxxx (redirect to direct download)
        // Already works as HTTP redirect — no transformation needed

        // ── S3 / Azure / GCS presigned URLs ──
        // Already direct download URLs — no transformation needed

        return url;
    }

    private static void CleanupTempFile(string path)
    {
        try { File.Delete(path); } catch { /* best-effort cleanup */ }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_ownsClient) _httpClient.Dispose();
    }
}
