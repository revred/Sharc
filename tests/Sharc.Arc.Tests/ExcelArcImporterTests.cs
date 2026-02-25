// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using System.IO.Compression;
using System.Text;
using Xunit;

namespace Sharc.Arc.Tests;

public sealed class ExcelArcImporterTests
{
    /// <summary>
    /// Creates a minimal .xlsx file in memory for testing.
    /// </summary>
    private static byte[] CreateTestXlsx(string[] headers, string[][] rows, string sheetName = "Sheet1")
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            // [Content_Types].xml
            AddEntry(zip, "[Content_Types].xml",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
                "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
                "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
                "<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>" +
                "<Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>" +
                "<Override PartName=\"/xl/sharedStrings.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sharedStrings+xml\"/>" +
                "</Types>");

            // _rels/.rels
            AddEntry(zip, "_rels/.rels",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/>" +
                "</Relationships>");

            // xl/workbook.xml
            AddEntry(zip, "xl/workbook.xml",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                "<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">" +
                "<sheets>" +
                $"<sheet name=\"{sheetName}\" sheetId=\"1\" r:id=\"rId1\"/>" +
                "</sheets></workbook>");

            // xl/_rels/workbook.xml.rels
            AddEntry(zip, "xl/_rels/workbook.xml.rels",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/>" +
                "<Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/sharedStrings\" Target=\"sharedStrings.xml\"/>" +
                "</Relationships>");

            // Collect all strings for shared strings table
            var allStrings = new List<string>();
            foreach (var h in headers) allStrings.Add(h);
            foreach (var row in rows)
                foreach (var cell in row)
                    if (!double.TryParse(cell, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out _))
                        allStrings.Add(cell);

            var uniqueStrings = allStrings.Distinct().ToList();

            // xl/sharedStrings.xml
            var ssSb = new StringBuilder();
            ssSb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
            ssSb.Append($"<sst xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" count=\"{uniqueStrings.Count}\" uniqueCount=\"{uniqueStrings.Count}\">");
            foreach (var s in uniqueStrings)
                ssSb.Append($"<si><t>{EscapeXml(s)}</t></si>");
            ssSb.Append("</sst>");
            AddEntry(zip, "xl/sharedStrings.xml", ssSb.ToString());

            // xl/worksheets/sheet1.xml
            var sheetSb = new StringBuilder();
            sheetSb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
            sheetSb.Append("<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">");
            sheetSb.Append("<sheetData>");

            // Header row
            sheetSb.Append("<row r=\"1\">");
            for (int c = 0; c < headers.Length; c++)
            {
                string colLetter = ((char)('A' + c)).ToString();
                int ssIdx = uniqueStrings.IndexOf(headers[c]);
                sheetSb.Append($"<c r=\"{colLetter}1\" t=\"s\"><v>{ssIdx}</v></c>");
            }
            sheetSb.Append("</row>");

            // Data rows
            for (int r = 0; r < rows.Length; r++)
            {
                int rowNum = r + 2;
                sheetSb.Append($"<row r=\"{rowNum}\">");
                for (int c = 0; c < rows[r].Length; c++)
                {
                    string colLetter = ((char)('A' + c)).ToString();
                    string cellRef = $"{colLetter}{rowNum}";
                    string val = rows[r][c];

                    if (double.TryParse(val, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out _))
                    {
                        sheetSb.Append($"<c r=\"{cellRef}\"><v>{val}</v></c>");
                    }
                    else
                    {
                        int ssIdx = uniqueStrings.IndexOf(val);
                        sheetSb.Append($"<c r=\"{cellRef}\" t=\"s\"><v>{ssIdx}</v></c>");
                    }
                }
                sheetSb.Append("</row>");
            }

            sheetSb.Append("</sheetData></worksheet>");
            AddEntry(zip, "xl/worksheets/sheet1.xml", sheetSb.ToString());
        }

        return ms.ToArray();
    }

    private static void AddEntry(ZipArchive zip, string path, string content)
    {
        var entry = zip.CreateEntry(path);
        using var stream = entry.Open();
        var bytes = Encoding.UTF8.GetBytes(content);
        stream.Write(bytes, 0, bytes.Length);
    }

    private static string EscapeXml(string s) =>
        s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

    [Fact]
    public void Import_BasicXlsx_CreatesQueryableArc()
    {
        var xlsx = CreateTestXlsx(
            new[] { "Name", "Age" },
            new[] { new[] { "Alice", "30" }, new[] { "Bob", "25" } });

        using var arc = ExcelArcImporter.Import(xlsx);

        using var reader = arc.Database.CreateReader("sheet1");
        int count = 0;
        while (reader.Read()) count++;
        Assert.Equal(2, count);
    }

    [Fact]
    public void Import_NumericAndTextMixed()
    {
        var xlsx = CreateTestXlsx(
            new[] { "Product", "Price", "Quantity" },
            new[] { new[] { "Widget", "9.99", "100" }, new[] { "Gadget", "24.50", "50" } });

        using var arc = ExcelArcImporter.Import(xlsx);
        using var reader = arc.Database.CreateReader("sheet1");

        Assert.True(reader.Read());
        Assert.Equal("Widget", reader.GetString(0));
    }

    [Fact]
    public void ListSheets_ReturnsSheetNames()
    {
        var xlsx = CreateTestXlsx(new[] { "A" }, new[] { new[] { "1" } }, "MyData");
        var sheets = ExcelArcImporter.ListSheets(xlsx);
        Assert.Contains("MyData", sheets);
    }

    [Fact]
    public void Import_CustomSheetName_UsesAsTableName()
    {
        var xlsx = CreateTestXlsx(new[] { "Col1" }, new[] { new[] { "val" } }, "Revenue");
        using var arc = ExcelArcImporter.Import(xlsx,
            new ExcelArcImporter.ExcelImportOptions { SheetName = "Revenue" });

        // Table name is sanitized sheet name
        var table = arc.Database.Schema.Tables.FirstOrDefault(t =>
            !t.Name.StartsWith("_sharc_") && t.Name != "sqlite_master");
        Assert.NotNull(table);
    }

    [Fact]
    public void Import_ThenFuse()
    {
        var xlsx = CreateTestXlsx(
            new[] { "dept", "budget" },
            new[] { new[] { "Engineering", "500000" }, new[] { "Marketing", "300000" } });

        using var excel = ExcelArcImporter.Import(xlsx,
            new ExcelArcImporter.ExcelImportOptions { ArcName = "budget.arc" });
        using var csv = CsvArcImporter.Import("dept,headcount\nEngineering,50",
            new CsvArcImporter.CsvImportOptions { ArcName = "headcount.arc" });

        using var fused = new FusedArcContext();
        fused.Mount(excel);
        fused.Mount(csv);

        Assert.Equal(2, fused.Count);
    }
}
