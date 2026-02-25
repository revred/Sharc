// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml;
using Sharc.Core;

namespace Sharc.Arc;

/// <summary>
/// Imports Microsoft Excel (.xlsx) files into .arc files.
/// Parses OOXML directly using <see cref="ZipArchive"/> and <see cref="XmlReader"/>
/// — zero external dependencies.
/// <para>
/// Handles: shared strings, inline strings, numeric cells, boolean cells,
/// date serial numbers, multiple sheets.
/// </para>
/// </summary>
public static class ExcelArcImporter
{
    /// <summary>Options for Excel import.</summary>
    public sealed class ExcelImportOptions
    {
        /// <summary>Sheet name to import. Null = first sheet.</summary>
        public string? SheetName { get; set; }

        /// <summary>Whether the first row is a header. Default: true.</summary>
        public bool HasHeader { get; set; } = true;

        /// <summary>Maximum rows to import (0 = unlimited).</summary>
        public int MaxRows { get; set; }

        /// <summary>Arc file name. Default: derived from source filename.</summary>
        public string ArcName { get; set; } = "imported.arc";
    }

    /// <summary>
    /// Imports an Excel file from a byte array.
    /// </summary>
    public static ArcHandle Import(byte[] xlsxBytes, ExcelImportOptions? options = null)
    {
        options ??= new ExcelImportOptions();

        using var stream = new MemoryStream(xlsxBytes);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);

        // 1. Load shared strings
        var sharedStrings = LoadSharedStrings(zip);

        // 2. Find the target sheet
        string sheetPath = FindSheetPath(zip, options.SheetName);

        // 3. Parse sheet into rows
        var rows = ParseSheet(zip, sheetPath, sharedStrings, options.MaxRows, options.HasHeader);
        if (rows.Data.Count == 0)
            throw new ArgumentException("Excel sheet contains no data rows.");

        // 4. Convert to CSV text and delegate to CsvArcImporter
        var csvText = ToCsv(rows.Headers, rows.Data);
        return CsvArcImporter.Import(csvText, new CsvArcImporter.CsvImportOptions
        {
            TableName = SanitizeTableName(rows.SheetName),
            HasHeader = true,
            MaxRows = options.MaxRows,
            ArcName = options.ArcName
        });
    }

    /// <summary>
    /// Imports an Excel file from disk.
    /// </summary>
    public static ArcHandle ImportFile(string path, ExcelImportOptions? options = null)
    {
        options ??= new ExcelImportOptions();
        if (options.ArcName == "imported.arc")
            options.ArcName = Path.GetFileNameWithoutExtension(path) + ".arc";
        return Import(File.ReadAllBytes(path), options);
    }

    /// <summary>
    /// Lists all sheet names in an Excel file.
    /// </summary>
    public static string[] ListSheets(byte[] xlsxBytes)
    {
        using var stream = new MemoryStream(xlsxBytes);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        return GetSheetNames(zip);
    }

    // ── Private helpers ──

    private static List<string> LoadSharedStrings(ZipArchive zip)
    {
        var strings = new List<string>();
        var entry = zip.GetEntry("xl/sharedStrings.xml");
        if (entry == null) return strings;

        var doc = new XmlDocument();
        using (var s = entry.Open())
            doc.Load(s);

        // Find all <si> elements regardless of namespace
        var siNodes = doc.GetElementsByTagName("si");
        foreach (XmlNode si in siNodes)
        {
            var sb = new StringBuilder();
            var tNodes = si.ChildNodes;
            CollectTextNodes(si, sb);
            strings.Add(sb.ToString());
        }

        return strings;
    }

    private static void CollectTextNodes(XmlNode parent, StringBuilder sb)
    {
        foreach (XmlNode child in parent.ChildNodes)
        {
            if (child.LocalName == "t")
                sb.Append(child.InnerText);
            else if (child.HasChildNodes)
                CollectTextNodes(child, sb);
        }
    }

    private static string FindSheetPath(ZipArchive zip, string? sheetName)
    {
        // Try workbook.xml to find sheet names and their rIds
        var wbEntry = zip.GetEntry("xl/workbook.xml");
        if (wbEntry == null)
            return "xl/worksheets/sheet1.xml"; // fallback

        var sheets = new List<(string name, string rId)>();
        using (var s = wbEntry.Open())
        using (var reader = XmlReader.Create(s))
        {
            while (reader.Read())
            {
                if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "sheet")
                {
                    string? name = reader.GetAttribute("name");
                    string? rId = reader.GetAttribute("id", "http://schemas.openxmlformats.org/officeDocument/2006/relationships");
                    if (name != null && rId != null)
                        sheets.Add((name, rId));
                }
            }
        }

        if (sheets.Count == 0)
            return "xl/worksheets/sheet1.xml";

        // Find the target sheet
        string targetRId;
        if (sheetName != null)
        {
            var match = sheets.FirstOrDefault(s => s.name.Equals(sheetName, StringComparison.OrdinalIgnoreCase));
            targetRId = match.rId ?? sheets[0].rId;
        }
        else
        {
            targetRId = sheets[0].rId;
        }

        // Resolve rId to file path via relationships
        var relsEntry = zip.GetEntry("xl/_rels/workbook.xml.rels");
        if (relsEntry != null)
        {
            using var rs = relsEntry.Open();
            using var reader = XmlReader.Create(rs);
            while (reader.Read())
            {
                if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "Relationship")
                {
                    if (reader.GetAttribute("Id") == targetRId)
                    {
                        string? target = reader.GetAttribute("Target");
                        if (target != null)
                            return "xl/" + target.TrimStart('/');
                    }
                }
            }
        }

        return "xl/worksheets/sheet1.xml";
    }

    private static string[] GetSheetNames(ZipArchive zip)
    {
        var wbEntry = zip.GetEntry("xl/workbook.xml");
        if (wbEntry == null) return Array.Empty<string>();

        var names = new List<string>();
        using var s = wbEntry.Open();
        using var reader = XmlReader.Create(s);

        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "sheet")
            {
                string? name = reader.GetAttribute("name");
                if (name != null) names.Add(name);
            }
        }

        return names.ToArray();
    }

    private static (string SheetName, string[] Headers, List<string[]> Data) ParseSheet(
        ZipArchive zip, string sheetPath, List<string> sharedStrings,
        int maxRows, bool hasHeader)
    {
        var entry = zip.GetEntry(sheetPath)
            ?? throw new ArgumentException($"Sheet not found at path: {sheetPath}");

        var allRows = new List<List<string>>();
        int maxCol = 0;

        using (var s = entry.Open())
        using (var reader = XmlReader.Create(s))
        {
            List<string>? currentRow = null;
            string? cellType = null;
            int cellCol = 0;

            while (reader.Read())
            {
                if (reader.NodeType == XmlNodeType.Element)
                {
                    switch (reader.LocalName)
                    {
                        case "row":
                            currentRow = new List<string>();
                            break;

                        case "c":
                            cellType = reader.GetAttribute("t");
                            string? cellRef = reader.GetAttribute("r");
                            cellCol = cellRef != null ? ParseColumnIndex(cellRef) : currentRow?.Count ?? 0;
                            // Pad missing columns
                            while (currentRow != null && currentRow.Count < cellCol)
                                currentRow.Add("");
                            break;

                        case "v":
                            string value = reader.ReadElementContentAsString();
                            string resolved = cellType switch
                            {
                                "s" => int.TryParse(value, out int idx) && idx < sharedStrings.Count
                                    ? sharedStrings[idx] : value,
                                "b" => value == "1" ? "TRUE" : "FALSE",
                                _ => value
                            };
                            currentRow?.Add(resolved);
                            maxCol = Math.Max(maxCol, currentRow?.Count ?? 0);
                            break;

                        case "is": // inline string
                            var sb = new StringBuilder();
                            while (reader.Read())
                            {
                                if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "t")
                                    sb.Append(reader.ReadElementContentAsString());
                                else if (reader.NodeType == XmlNodeType.EndElement && reader.LocalName == "is")
                                    break;
                            }
                            currentRow?.Add(sb.ToString());
                            maxCol = Math.Max(maxCol, currentRow?.Count ?? 0);
                            break;
                    }
                }
                else if (reader.NodeType == XmlNodeType.EndElement && reader.LocalName == "row")
                {
                    if (currentRow != null)
                    {
                        allRows.Add(currentRow);
                        currentRow = null;
                    }
                }
            }
        }

        // Normalize row widths
        foreach (var row in allRows)
            while (row.Count < maxCol) row.Add("");

        // Split headers and data
        string[] headers;
        var data = new List<string[]>();

        if (hasHeader && allRows.Count > 0)
        {
            headers = allRows[0].ToArray();
            for (int i = 1; i < allRows.Count; i++)
                data.Add(allRows[i].ToArray());
        }
        else
        {
            headers = new string[maxCol];
            for (int i = 0; i < maxCol; i++) headers[i] = $"col{i + 1}";
            foreach (var row in allRows)
                data.Add(row.ToArray());
        }

        string sheetName = Path.GetFileNameWithoutExtension(sheetPath);
        return (sheetName, headers, data);
    }

    private static int ParseColumnIndex(string cellRef)
    {
        int col = 0;
        foreach (char c in cellRef)
        {
            if (char.IsLetter(c))
                col = col * 26 + (char.ToUpperInvariant(c) - 'A' + 1);
            else
                break;
        }
        return col - 1; // 0-based
    }

    private static string ToCsv(string[] headers, List<string[]> data)
    {
        var sb = new StringBuilder();

        // Header
        for (int i = 0; i < headers.Length; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append(CsvEscape(headers[i]));
        }
        sb.AppendLine();

        // Data
        foreach (var row in data)
        {
            for (int i = 0; i < row.Length; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(CsvEscape(row[i]));
            }
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static string CsvEscape(string value)
    {
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        return value;
    }

    private static string SanitizeTableName(string name)
    {
        var sb = new StringBuilder(name.Length);
        foreach (char c in name)
            sb.Append(char.IsLetterOrDigit(c) || c == '_' ? c : '_');
        string result = sb.ToString();
        return result.Length == 0 || char.IsDigit(result[0]) ? "_" + result : result;
    }
}
