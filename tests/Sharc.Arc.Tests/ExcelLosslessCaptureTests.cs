// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using System.IO.Compression;
using System.Text;
using Xunit;

namespace Sharc.Arc.Tests;

public sealed class ExcelLosslessCaptureTests
{
    /// <summary>Creates a minimal XLSX with formulas and styles for testing.</summary>
    private static byte[] CreateTestXlsxWithFormulas()
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            Add(zip, "[Content_Types].xml",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
                "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
                "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
                "<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>" +
                "<Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>" +
                "<Override PartName=\"/xl/sharedStrings.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sharedStrings+xml\"/>" +
                "<Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>" +
                "</Types>");

            Add(zip, "_rels/.rels",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/>" +
                "</Relationships>");

            Add(zip, "xl/workbook.xml",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                "<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">" +
                "<sheets><sheet name=\"Budget\" sheetId=\"1\" r:id=\"rId1\"/></sheets>" +
                "<definedNames><definedName name=\"TotalBudget\" localSheetId=\"0\">Budget!$C$4</definedName></definedNames>" +
                "</workbook>");

            Add(zip, "xl/_rels/workbook.xml.rels",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/>" +
                "</Relationships>");

            // Shared strings: Item, Q1, Q2, Total
            Add(zip, "xl/sharedStrings.xml",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                "<sst xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" count=\"4\" uniqueCount=\"4\">" +
                "<si><t>Item</t></si><si><t>Q1</t></si><si><t>Q2</t></si><si><t>Total</t></si></sst>");

            // Styles with bold font
            Add(zip, "xl/styles.xml",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                "<styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">" +
                "<fonts count=\"2\"><font><name val=\"Calibri\"/><sz val=\"11\"/></font>" +
                "<font><name val=\"Calibri\"/><sz val=\"11\"/><b/></font></fonts>" +
                "<fills count=\"2\"><fill><patternFill patternType=\"none\"/></fill>" +
                "<fill><patternFill patternType=\"gray125\"/></fill></fills>" +
                "<borders count=\"1\"><border/></borders>" +
                "<cellXfs count=\"2\">" +
                "<xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\"/>" +
                "<xf numFmtId=\"0\" fontId=\"1\" fillId=\"0\"/>" +  // bold style
                "</cellXfs></styleSheet>");

            // Sheet with header, values, and formulas
            // Row 1: Item(s), Q1(s), Q2(s), Total(s) — headers (bold, style 1)
            // Row 2: (empty - "Marketing"), 5000, 6000, =B2+C2
            // Row 3: (empty - "Engineering"), 8000, 9000, =B3+C3
            // Row 4: (empty - "Total"), =SUM(B2:B3), =SUM(C2:C3), =SUM(D2:D3)
            Add(zip, "xl/worksheets/sheet1.xml",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                "<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">" +
                "<sheetData>" +
                "<row r=\"1\">" +
                "<c r=\"A1\" t=\"s\" s=\"1\"><v>0</v></c>" +
                "<c r=\"B1\" t=\"s\" s=\"1\"><v>1</v></c>" +
                "<c r=\"C1\" t=\"s\" s=\"1\"><v>2</v></c>" +
                "<c r=\"D1\" t=\"s\" s=\"1\"><v>3</v></c>" +
                "</row>" +
                "<row r=\"2\">" +
                "<c r=\"A2\" t=\"s\"><v>0</v></c>" +
                "<c r=\"B2\"><v>5000</v></c>" +
                "<c r=\"C2\"><v>6000</v></c>" +
                "<c r=\"D2\"><f>B2+C2</f><v>11000</v></c>" +
                "</row>" +
                "<row r=\"3\">" +
                "<c r=\"A3\" t=\"s\"><v>0</v></c>" +
                "<c r=\"B3\"><v>8000</v></c>" +
                "<c r=\"C3\"><v>9000</v></c>" +
                "<c r=\"D3\"><f>B3+C3</f><v>17000</v></c>" +
                "</row>" +
                "<row r=\"4\">" +
                "<c r=\"A4\" t=\"s\"><v>0</v></c>" +
                "<c r=\"B4\"><f>SUM(B2:B3)</f><v>13000</v></c>" +
                "<c r=\"C4\"><f>SUM(C2:C3)</f><v>15000</v></c>" +
                "<c r=\"D4\"><f>SUM(D2:D3)</f><v>28000</v></c>" +
                "</row>" +
                "</sheetData>" +
                "<mergeCells count=\"1\"><mergeCell ref=\"A1:D1\"/></mergeCells>" +
                "</worksheet>");
        }
        return ms.ToArray();
    }

    private static void Add(ZipArchive zip, string path, string content)
    {
        var e = zip.CreateEntry(path);
        using var s = e.Open();
        s.Write(Encoding.UTF8.GetBytes(content));
    }

    [Fact]
    public void Capture_CreatesAllTables()
    {
        var xlsx = CreateTestXlsxWithFormulas();
        using var arc = ExcelLosslessCapture.Capture(xlsx);

        var tables = arc.Database.Schema.Tables.Select(t => t.Name).ToList();
        Assert.Contains("cells", tables);
        Assert.Contains("styles", tables);
        Assert.Contains("cell_references", tables);
        Assert.Contains("sheets", tables);
        Assert.Contains("merged_regions", tables);
        Assert.Contains("defined_names", tables);
    }

    [Fact]
    public void Capture_SheetMetadata()
    {
        var xlsx = CreateTestXlsxWithFormulas();
        using var arc = ExcelLosslessCapture.Capture(xlsx);

        using var reader = arc.Database.CreateReader("sheets");
        Assert.True(reader.Read());
        Assert.Equal("Budget", reader.GetString(1)); // name
    }

    [Fact]
    public void Capture_CellValues()
    {
        var xlsx = CreateTestXlsxWithFormulas();
        using var arc = ExcelLosslessCapture.Capture(xlsx);

        using var reader = arc.Database.CreateReader("cells");
        int count = 0;
        while (reader.Read()) count++;
        Assert.Equal(16, count); // 4 rows × 4 cols
    }

    [Fact]
    public void Capture_Formulas()
    {
        var xlsx = CreateTestXlsxWithFormulas();
        using var arc = ExcelLosslessCapture.Capture(xlsx);

        // Query cells that have formulas
        using var reader = arc.Database.CreateReader("cells");
        var formulas = new List<string>();
        while (reader.Read())
        {
            if (!reader.IsNull(6)) // formula column
                formulas.Add(reader.GetString(6));
        }
        Assert.Equal(5, formulas.Count); // B2+C2, B3+C3, SUM(B2:B3), SUM(C2:C3), SUM(D2:D3)
        Assert.Contains("B2+C2", formulas);
        Assert.Contains("SUM(B2:B3)", formulas);
    }

    [Fact]
    public void Capture_Styles()
    {
        var xlsx = CreateTestXlsxWithFormulas();
        using var arc = ExcelLosslessCapture.Capture(xlsx);

        using var reader = arc.Database.CreateReader("styles");
        int count = 0;
        bool foundBold = false;
        while (reader.Read())
        {
            count++;
            if (!reader.IsNull(5) && reader.GetInt64(5) == 1) // font_bold
                foundBold = true;
        }
        Assert.Equal(2, count); // 2 styles (normal + bold)
        Assert.True(foundBold);
    }

    [Fact]
    public void Capture_CellReferences()
    {
        var xlsx = CreateTestXlsxWithFormulas();
        using var arc = ExcelLosslessCapture.Capture(xlsx);

        using var reader = arc.Database.CreateReader("cell_references");
        int count = 0;
        while (reader.Read()) count++;
        Assert.True(count > 0); // Should have references from formula cells
    }

    [Fact]
    public void Capture_MergedRegions()
    {
        var xlsx = CreateTestXlsxWithFormulas();
        using var arc = ExcelLosslessCapture.Capture(xlsx);

        using var reader = arc.Database.CreateReader("merged_regions");
        Assert.True(reader.Read());
        Assert.Equal("A1:D1", reader.GetString(1)); // range_ref
    }

    [Fact]
    public void Capture_DefinedNames()
    {
        var xlsx = CreateTestXlsxWithFormulas();
        using var arc = ExcelLosslessCapture.Capture(xlsx);

        using var reader = arc.Database.CreateReader("defined_names");
        Assert.True(reader.Read());
        Assert.Equal("TotalBudget", reader.GetString(0));
        Assert.Contains("$C$4", reader.GetString(2)); // formula
    }

    [Fact]
    public void ExtractCellReferences_SimpleFormula()
    {
        var refs = ExcelLosslessCapture.ExtractCellReferences("B2+C2");
        Assert.Contains("B2", refs);
        Assert.Contains("C2", refs);
    }

    [Fact]
    public void ExtractCellReferences_SumRange()
    {
        var refs = ExcelLosslessCapture.ExtractCellReferences("SUM(B2:B3)");
        Assert.Contains("B2", refs);
        Assert.Contains("B3", refs);
    }

    [Fact]
    public void ExtractCellReferences_AbsoluteRef()
    {
        var refs = ExcelLosslessCapture.ExtractCellReferences("$A$1+B2");
        Assert.Contains("$A$1", refs);
        Assert.Contains("B2", refs);
    }

    [Fact]
    public void Capture_ThenFuse()
    {
        var xlsx = CreateTestXlsxWithFormulas();
        using var captured = ExcelLosslessCapture.Capture(xlsx,
            new ExcelLosslessCapture.LosslessCaptureOptions { ArcName = "budget.arc" });
        using var csv = CsvArcImporter.Import("team,role\nAlice,PM",
            new CsvArcImporter.CsvImportOptions { ArcName = "team.arc" });

        using var fused = new FusedArcContext();
        fused.Mount(captured);
        fused.Mount(csv);

        var tables = fused.DiscoverTables();
        Assert.True(tables.ContainsKey("cells"));
        Assert.True(tables.ContainsKey("data"));
    }
}
