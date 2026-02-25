// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

namespace Sharc.Arc;

/// <summary>
/// Imports Google Sheets into .arc files by transforming the sharing URL
/// into a CSV export URL and delegating to <see cref="CsvArcImporter"/>.
/// <para>
/// <b>No API key required</b> — works with publicly shared sheets or
/// anyone-with-link sheets. For private sheets, use the export-as-CSV
/// feature in Google Sheets and import via <see cref="CsvArcImporter"/>.
/// </para>
/// </summary>
public static class GoogleSheetsImporter
{
    /// <summary>Options for Google Sheets import.</summary>
    public sealed class GoogleSheetsImportOptions
    {
        /// <summary>Table name for imported data. Default: "data".</summary>
        public string TableName { get; set; } = "data";

        /// <summary>Specific sheet/tab name (gid). Null = first sheet.</summary>
        public string? SheetGid { get; set; }

        /// <summary>Maximum rows (0 = unlimited).</summary>
        public int MaxRows { get; set; }

        /// <summary>Arc file name.</summary>
        public string ArcName { get; set; } = "imported.arc";

        /// <summary>HTTP timeout in seconds. Default: 30.</summary>
        public int TimeoutSeconds { get; set; } = 30;
    }

    /// <summary>
    /// Imports a Google Sheet by its sharing URL or spreadsheet ID.
    /// </summary>
    /// <param name="urlOrId">
    /// One of:
    /// <list type="bullet">
    ///   <item>Full URL: <c>https://docs.google.com/spreadsheets/d/{ID}/edit#gid=0</c></item>
    ///   <item>Spreadsheet ID: <c>1BxiMVs0XRA5nFMdKvBdBZjgmUUqptlbs74OgVE2upms</c></item>
    /// </list>
    /// </param>
    /// <param name="options">Import options.</param>
    /// <param name="httpClient">Optional HttpClient (for testing/reuse).</param>
    public static async Task<ArcHandle> ImportAsync(string urlOrId,
        GoogleSheetsImportOptions? options = null, HttpClient? httpClient = null)
    {
        options ??= new GoogleSheetsImportOptions();

        string spreadsheetId = ExtractSpreadsheetId(urlOrId);
        string csvUrl = BuildCsvExportUrl(spreadsheetId, options.SheetGid);

        bool ownsClient = httpClient == null;
        httpClient ??= new HttpClient();

        try
        {
            httpClient.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
            string csvText = await httpClient.GetStringAsync(csvUrl).ConfigureAwait(false);

            return CsvArcImporter.Import(csvText, new CsvArcImporter.CsvImportOptions
            {
                TableName = options.TableName,
                HasHeader = true,
                MaxRows = options.MaxRows,
                ArcName = options.ArcName
            });
        }
        finally
        {
            if (ownsClient) httpClient.Dispose();
        }
    }

    /// <summary>
    /// Extracts the spreadsheet ID from a Google Sheets URL or returns the input if already an ID.
    /// </summary>
    public static string ExtractSpreadsheetId(string urlOrId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(urlOrId);

        // Already just an ID (no slashes, no dots, alphanumeric + dashes + underscores)
        if (!urlOrId.Contains('/') && !urlOrId.Contains('.'))
            return urlOrId;

        // URL patterns:
        // https://docs.google.com/spreadsheets/d/{ID}/edit#gid=0
        // https://docs.google.com/spreadsheets/d/{ID}/
        // https://docs.google.com/spreadsheets/d/{ID}
        const string marker = "/spreadsheets/d/";
        int idx = urlOrId.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (idx < 0)
            throw new ArgumentException(
                $"Cannot extract spreadsheet ID from: '{urlOrId}'. " +
                "Expected a Google Sheets URL or a bare spreadsheet ID.");

        int start = idx + marker.Length;
        int end = urlOrId.IndexOf('/', start);
        if (end < 0) end = urlOrId.IndexOf('?', start);
        if (end < 0) end = urlOrId.IndexOf('#', start);
        if (end < 0) end = urlOrId.Length;

        return urlOrId[start..end];
    }

    /// <summary>
    /// Builds the CSV export URL for a Google Sheet.
    /// </summary>
    public static string BuildCsvExportUrl(string spreadsheetId, string? sheetGid = null)
    {
        string url = $"https://docs.google.com/spreadsheets/d/{spreadsheetId}/export?format=csv";
        if (sheetGid != null)
            url += $"&gid={sheetGid}";
        return url;
    }
}
