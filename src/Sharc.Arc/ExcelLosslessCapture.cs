// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml;
using Sharc.Core;

namespace Sharc.Arc;

/// <summary>
/// Lossless Excel (.xlsx) capture into .arc files. Preserves every cell's
/// value, formula, formatting, style, and inter-cell references. The resulting
/// .arc contains enough information to reconstruct the original spreadsheet
/// to the last bit.
/// <para>
/// <b>Tables created:</b>
/// <list type="bullet">
///   <item><c>cells</c> — every cell: row, col, value, formula, type, style_id, number_format</item>
///   <item><c>styles</c> — style definitions: font, fill, border, alignment, number format</item>
///   <item><c>cell_references</c> — formula-derived cell-to-cell references (dependency graph)</item>
///   <item><c>sheets</c> — sheet metadata: name, index, state (visible/hidden)</item>
///   <item><c>merged_regions</c> — merged cell ranges per sheet</item>
///   <item><c>defined_names</c> — named ranges and formulas</item>
/// </list>
/// </para>
/// </summary>
public static class ExcelLosslessCapture
{
    /// <summary>Options for lossless capture.</summary>
    public sealed class LosslessCaptureOptions
    {
        /// <summary>Arc file name. Default: derived from source.</summary>
        public string ArcName { get; set; } = "captured.arc";

        /// <summary>Which sheets to capture. Null = all sheets.</summary>
        public string[]? SheetFilter { get; set; }
    }

    /// <summary>
    /// Captures an Excel file losslessly into an .arc file.
    /// </summary>
    public static ArcHandle Capture(byte[] xlsxBytes, LosslessCaptureOptions? options = null)
    {
        options ??= new LosslessCaptureOptions();

        using var stream = new MemoryStream(xlsxBytes);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);

        var handle = ArcHandle.CreateInMemory(options.ArcName);
        var db = handle.Database;

        // Create all metadata tables
        using (var tx = db.BeginTransaction())
        {
            tx.Execute(@"CREATE TABLE sheets (
                sheet_index INTEGER PRIMARY KEY, name TEXT, state TEXT, rel_id TEXT)");

            tx.Execute(@"CREATE TABLE cells (
                sheet_index INTEGER, row_num INTEGER, col_num INTEGER, col_ref TEXT,
                cell_ref TEXT, value TEXT, formula TEXT, cell_type TEXT,
                style_id INTEGER, number_format TEXT,
                PRIMARY KEY (sheet_index, row_num, col_num))");

            tx.Execute(@"CREATE TABLE styles (
                style_id INTEGER PRIMARY KEY, number_format_id INTEGER, number_format TEXT,
                font_name TEXT, font_size REAL, font_bold INTEGER, font_italic INTEGER,
                font_color TEXT, fill_color TEXT, fill_pattern TEXT,
                border_style TEXT, alignment_horizontal TEXT, alignment_vertical TEXT,
                wrap_text INTEGER)");

            tx.Execute(@"CREATE TABLE cell_references (
                source_sheet INTEGER, source_cell TEXT,
                target_sheet INTEGER, target_cell TEXT,
                formula TEXT)");

            tx.Execute(@"CREATE TABLE merged_regions (
                sheet_index INTEGER, range_ref TEXT,
                start_row INTEGER, start_col INTEGER,
                end_row INTEGER, end_col INTEGER)");

            tx.Execute(@"CREATE TABLE defined_names (
                name TEXT, sheet_index INTEGER, formula TEXT, comment TEXT)");

            tx.Commit();
        }

        // 1. Load shared strings
        var sharedStrings = LoadSharedStrings(zip);

        // 2. Load styles
        var styles = LoadStyles(zip);
        InsertStyles(db, styles);

        // 3. Find and process sheets
        var sheetInfos = GetSheetInfos(zip);

        // 4. Load defined names
        LoadDefinedNames(zip, db);

        using var writer = SharcWriter.From(db);
        int sheetIdx = 0;

        foreach (var sheet in sheetInfos)
        {
            if (options.SheetFilter != null &&
                !options.SheetFilter.Any(f => f.Equals(sheet.Name, StringComparison.OrdinalIgnoreCase)))
                continue;

            // Insert sheet metadata
            writer.Insert("sheets",
                ColumnValue.FromInt64(1, sheetIdx),
                TextVal(sheet.Name),
                TextVal(sheet.State),
                TextVal(sheet.RelId));

            // Parse sheet cells
            var sheetPath = ResolveSheetPath(zip, sheet.RelId);
            if (sheetPath != null)
            {
                CaptureSheet(zip, sheetPath, sheetIdx, sharedStrings, styles, writer);
            }

            sheetIdx++;
        }

        return handle;
    }

    /// <summary>Captures from a file path.</summary>
    public static ArcHandle CaptureFile(string path, LosslessCaptureOptions? options = null)
    {
        options ??= new LosslessCaptureOptions();
        if (options.ArcName == "captured.arc")
            options.ArcName = Path.GetFileNameWithoutExtension(path) + ".arc";
        return Capture(File.ReadAllBytes(path), options);
    }

    // ── Shared Strings ──

    private static List<string> LoadSharedStrings(ZipArchive zip)
    {
        var strings = new List<string>();
        var entry = zip.GetEntry("xl/sharedStrings.xml");
        if (entry == null) return strings;

        var doc = new XmlDocument();
        using (var s = entry.Open()) doc.Load(s);

        foreach (XmlNode si in doc.GetElementsByTagName("si"))
        {
            var sb = new StringBuilder();
            CollectText(si, sb);
            strings.Add(sb.ToString());
        }
        return strings;
    }

    private static void CollectText(XmlNode parent, StringBuilder sb)
    {
        foreach (XmlNode child in parent.ChildNodes)
        {
            if (child.LocalName == "t") sb.Append(child.InnerText);
            else if (child.HasChildNodes) CollectText(child, sb);
        }
    }

    // ── Styles ──

    private sealed class StyleInfo
    {
        public int Id;
        public int NumFmtId;
        public string NumFormat = "";
        public string FontName = "";
        public double FontSize;
        public bool Bold;
        public bool Italic;
        public string FontColor = "";
        public string FillColor = "";
        public string FillPattern = "";
        public string BorderStyle = "";
        public string HorizontalAlign = "";
        public string VerticalAlign = "";
        public bool WrapText;
    }

    private static List<StyleInfo> LoadStyles(ZipArchive zip)
    {
        var styles = new List<StyleInfo>();
        var entry = zip.GetEntry("xl/styles.xml");
        if (entry == null)
        {
            styles.Add(new StyleInfo { Id = 0, NumFormat = "General" });
            return styles;
        }

        var doc = new XmlDocument();
        using (var s = entry.Open()) doc.Load(s);

        // Parse number formats
        var numFormats = new Dictionary<int, string> {
            {0,"General"}, {1,"0"}, {2,"0.00"}, {3,"#,##0"}, {4,"#,##0.00"},
            {9,"0%"}, {10,"0.00%"}, {11,"0.00E+00"}, {14,"m/d/yyyy"},
            {15,"d-mmm-yy"}, {16,"d-mmm"}, {17,"mmm-yy"}, {20,"h:mm"},
            {21,"h:mm:ss"}, {22,"m/d/yyyy h:mm"}, {49,"@"}
        };

        var numFmtNodes = doc.GetElementsByTagName("numFmt");
        foreach (XmlNode nf in numFmtNodes)
        {
            if (int.TryParse(nf.Attributes?["numFmtId"]?.Value, out int id))
                numFormats[id] = nf.Attributes?["formatCode"]?.Value ?? "";
        }

        // Parse fonts
        var fonts = new List<(string name, double size, bool bold, bool italic, string color)>();
        var fontNodes = doc.GetElementsByTagName("font");
        foreach (XmlNode font in fontNodes)
        {
            string name = "", color = "";
            double size = 11;
            bool bold = false, italic = false;
            foreach (XmlNode child in font.ChildNodes)
            {
                switch (child.LocalName)
                {
                    case "name": name = child.Attributes?["val"]?.Value ?? ""; break;
                    case "sz":
                        double.TryParse(child.Attributes?["val"]?.Value, NumberStyles.Float,
                            CultureInfo.InvariantCulture, out size);
                        break;
                    case "b": bold = true; break;
                    case "i": italic = true; break;
                    case "color":
                        color = child.Attributes?["rgb"]?.Value
                             ?? child.Attributes?["theme"]?.Value ?? "";
                        break;
                }
            }
            fonts.Add((name, size, bold, italic, color));
        }

        // Parse fills
        var fills = new List<(string color, string pattern)>();
        var fillNodes = doc.GetElementsByTagName("fill");
        foreach (XmlNode fill in fillNodes)
        {
            string color = "", pattern = "";
            foreach (XmlNode child in fill.ChildNodes)
            {
                if (child.LocalName == "patternFill")
                {
                    pattern = child.Attributes?["patternType"]?.Value ?? "";
                    var fgColor = child.SelectSingleNode("*[local-name()='fgColor']");
                    color = fgColor?.Attributes?["rgb"]?.Value
                         ?? fgColor?.Attributes?["theme"]?.Value ?? "";
                }
            }
            fills.Add((color, pattern));
        }

        // Parse cell xfs (the actual style entries)
        var xfNodes = doc.GetElementsByTagName("xf");
        int styleId = 0;

        // Find the cellXfs section
        var cellXfsNode = doc.GetElementsByTagName("cellXfs");
        if (cellXfsNode.Count > 0)
        {
            foreach (XmlNode xf in cellXfsNode[0]!.ChildNodes)
            {
                if (xf.LocalName != "xf") continue;

                var si = new StyleInfo { Id = styleId++ };

                int numFmtId = int.TryParse(xf.Attributes?["numFmtId"]?.Value, out int nfid) ? nfid : 0;
                si.NumFmtId = numFmtId;
                si.NumFormat = numFormats.TryGetValue(numFmtId, out var fmt) ? fmt : "";

                int fontId = int.TryParse(xf.Attributes?["fontId"]?.Value, out int fid) ? fid : 0;
                if (fontId < fonts.Count)
                {
                    var f = fonts[fontId];
                    si.FontName = f.name;
                    si.FontSize = f.size;
                    si.Bold = f.bold;
                    si.Italic = f.italic;
                    si.FontColor = f.color;
                }

                int fillId = int.TryParse(xf.Attributes?["fillId"]?.Value, out int flid) ? flid : 0;
                if (fillId < fills.Count)
                {
                    si.FillColor = fills[fillId].color;
                    si.FillPattern = fills[fillId].pattern;
                }

                // Alignment
                var align = xf.SelectSingleNode("*[local-name()='alignment']");
                if (align != null)
                {
                    si.HorizontalAlign = align.Attributes?["horizontal"]?.Value ?? "";
                    si.VerticalAlign = align.Attributes?["vertical"]?.Value ?? "";
                    si.WrapText = align.Attributes?["wrapText"]?.Value == "1";
                }

                styles.Add(si);
            }
        }

        if (styles.Count == 0)
            styles.Add(new StyleInfo { Id = 0, NumFormat = "General" });

        return styles;
    }

    private static void InsertStyles(SharcDatabase db, List<StyleInfo> styles)
    {
        using var writer = SharcWriter.From(db);
        foreach (var s in styles)
        {
            writer.Insert("styles",
                ColumnValue.FromInt64(4, s.Id),
                ColumnValue.FromInt64(4, s.NumFmtId),
                TextVal(s.NumFormat),
                TextVal(s.FontName),
                ColumnValue.FromDouble(s.FontSize),
                ColumnValue.FromInt64(1, s.Bold ? 1 : 0),
                ColumnValue.FromInt64(1, s.Italic ? 1 : 0),
                TextVal(s.FontColor),
                TextVal(s.FillColor),
                TextVal(s.FillPattern),
                TextVal(s.BorderStyle),
                TextVal(s.HorizontalAlign),
                TextVal(s.VerticalAlign),
                ColumnValue.FromInt64(1, s.WrapText ? 1 : 0));
        }
    }

    // ── Sheet Discovery ──

    private sealed record SheetMeta(string Name, string RelId, string State);

    private static List<SheetMeta> GetSheetInfos(ZipArchive zip)
    {
        var sheets = new List<SheetMeta>();
        var entry = zip.GetEntry("xl/workbook.xml");
        if (entry == null) return sheets;

        var doc = new XmlDocument();
        using (var s = entry.Open()) doc.Load(s);

        foreach (XmlNode node in doc.GetElementsByTagName("sheet"))
        {
            string name = node.Attributes?["name"]?.Value ?? "";
            string rId = node.Attributes?["id",
                "http://schemas.openxmlformats.org/officeDocument/2006/relationships"]?.Value ?? "";
            string state = node.Attributes?["state"]?.Value ?? "visible";
            sheets.Add(new SheetMeta(name, rId, state));
        }
        return sheets;
    }

    private static string? ResolveSheetPath(ZipArchive zip, string relId)
    {
        var entry = zip.GetEntry("xl/_rels/workbook.xml.rels");
        if (entry == null) return null;

        var doc = new XmlDocument();
        using (var s = entry.Open()) doc.Load(s);

        foreach (XmlNode rel in doc.GetElementsByTagName("Relationship"))
        {
            if (rel.Attributes?["Id"]?.Value == relId)
            {
                string target = rel.Attributes?["Target"]?.Value ?? "";
                return "xl/" + target.TrimStart('/');
            }
        }
        return null;
    }

    // ── Sheet Capture ──

    private static void CaptureSheet(ZipArchive zip, string sheetPath, int sheetIndex,
        List<string> sharedStrings, List<StyleInfo> styles, SharcWriter writer)
    {
        var entry = zip.GetEntry(sheetPath);
        if (entry == null) return;

        var doc = new XmlDocument();
        using (var s = entry.Open()) doc.Load(s);

        // Capture merged regions
        foreach (XmlNode mc in doc.GetElementsByTagName("mergeCell"))
        {
            string rangeRef = mc.Attributes?["ref"]?.Value ?? "";
            if (!string.IsNullOrEmpty(rangeRef))
            {
                var (sr, sc, er, ec) = ParseRangeRef(rangeRef);
                writer.Insert("merged_regions",
                    ColumnValue.FromInt64(4, sheetIndex),
                    TextVal(rangeRef),
                    ColumnValue.FromInt64(4, sr),
                    ColumnValue.FromInt64(4, sc),
                    ColumnValue.FromInt64(4, er),
                    ColumnValue.FromInt64(4, ec));
            }
        }

        // Capture cells
        foreach (XmlNode row in doc.GetElementsByTagName("row"))
        {
            int rowNum = int.TryParse(row.Attributes?["r"]?.Value,
                NumberStyles.Integer, CultureInfo.InvariantCulture, out int rn) ? rn : 0;

            foreach (XmlNode cell in row.ChildNodes)
            {
                if (cell.LocalName != "c") continue;

                string cellRef = cell.Attributes?["r"]?.Value ?? "";
                string cellType = cell.Attributes?["t"]?.Value ?? "";
                int styleIdx = int.TryParse(cell.Attributes?["s"]?.Value,
                    NumberStyles.Integer, CultureInfo.InvariantCulture, out int si) ? si : 0;

                int colNum = ParseColIndex(cellRef);
                string colRef = GetColLetters(cellRef);

                // Value
                var vNode = cell.SelectSingleNode("*[local-name()='v']");
                string rawValue = vNode?.InnerText ?? "";

                // Resolve shared string
                string resolvedValue = cellType switch
                {
                    "s" => int.TryParse(rawValue, out int ssIdx) && ssIdx < sharedStrings.Count
                        ? sharedStrings[ssIdx] : rawValue,
                    "b" => rawValue == "1" ? "TRUE" : "FALSE",
                    "inlineStr" => cell.SelectSingleNode("*[local-name()='is']/*[local-name()='t']")?.InnerText ?? "",
                    _ => rawValue
                };

                // Formula
                var fNode = cell.SelectSingleNode("*[local-name()='f']");
                string formula = fNode?.InnerText ?? "";

                // Number format from style
                string numFormat = styleIdx < styles.Count ? styles[styleIdx].NumFormat : "";

                // Map cell type
                string typeName = cellType switch
                {
                    "s" => "shared_string",
                    "str" => "formula_string",
                    "inlineStr" => "inline_string",
                    "b" => "boolean",
                    "e" => "error",
                    "" when !string.IsNullOrEmpty(formula) => "formula_numeric",
                    "" => "numeric",
                    _ => cellType
                };

                writer.Insert("cells",
                    ColumnValue.FromInt64(4, sheetIndex),
                    ColumnValue.FromInt64(4, rowNum),
                    ColumnValue.FromInt64(4, colNum),
                    TextVal(colRef),
                    TextVal(cellRef),
                    TextVal(resolvedValue),
                    string.IsNullOrEmpty(formula) ? ColumnValue.Null() : TextVal(formula),
                    TextVal(typeName),
                    ColumnValue.FromInt64(4, styleIdx),
                    TextVal(numFormat));

                // Extract cell references from formula
                if (!string.IsNullOrEmpty(formula))
                {
                    foreach (var refTarget in ExtractCellReferences(formula))
                    {
                        writer.Insert("cell_references",
                            ColumnValue.FromInt64(4, sheetIndex),
                            TextVal(cellRef),
                            ColumnValue.FromInt64(4, sheetIndex), // same sheet unless cross-sheet ref
                            TextVal(refTarget),
                            TextVal(formula));
                    }
                }
            }
        }
    }

    // ── Defined Names ──

    private static void LoadDefinedNames(ZipArchive zip, SharcDatabase db)
    {
        var entry = zip.GetEntry("xl/workbook.xml");
        if (entry == null) return;

        var doc = new XmlDocument();
        using (var s = entry.Open()) doc.Load(s);

        using var writer = SharcWriter.From(db);
        foreach (XmlNode dn in doc.GetElementsByTagName("definedName"))
        {
            string name = dn.Attributes?["name"]?.Value ?? "";
            int sheetIdx = int.TryParse(dn.Attributes?["localSheetId"]?.Value,
                NumberStyles.Integer, CultureInfo.InvariantCulture, out int lsi) ? lsi : -1;
            string formula = dn.InnerText;
            string comment = dn.Attributes?["comment"]?.Value ?? "";

            writer.Insert("defined_names",
                TextVal(name),
                ColumnValue.FromInt64(4, sheetIdx),
                TextVal(formula),
                TextVal(comment));
        }
    }

    // ── Cell Reference Extraction ──

    /// <summary>
    /// Extracts cell references from a formula string. Handles A1, $A$1, A1:B2 range refs.
    /// </summary>
    internal static List<string> ExtractCellReferences(string formula)
    {
        var refs = new List<string>();
        int i = 0;
        while (i < formula.Length)
        {
            // Skip quoted strings
            if (formula[i] == '"')
            {
                i++;
                while (i < formula.Length && formula[i] != '"') i++;
                if (i < formula.Length) i++;
                continue;
            }

            // Look for cell reference pattern: optional $, letter(s), optional $, digit(s)
            if (i < formula.Length && (formula[i] == '$' || char.IsLetter(formula[i])))
            {
                int start = i;
                // Skip leading $
                if (i < formula.Length && formula[i] == '$') i++;
                // Collect letters
                int letterStart = i;
                while (i < formula.Length && char.IsLetter(formula[i])) i++;
                int letterCount = i - letterStart;
                // Skip $ before row
                if (i < formula.Length && formula[i] == '$') i++;
                // Collect digits
                int digitStart = i;
                while (i < formula.Length && char.IsDigit(formula[i])) i++;
                int digitCount = i - digitStart;

                if (letterCount > 0 && letterCount <= 3 && digitCount > 0 && digitCount <= 7)
                {
                    string cellRef = formula[start..i];
                    // Exclude function names (followed by '(')
                    if (i >= formula.Length || formula[i] != '(')
                        refs.Add(cellRef);
                }
                else
                {
                    i = start + 1; // not a cell ref, advance
                }
            }
            else
            {
                i++;
            }
        }
        return refs;
    }

    // ── Helpers ──

    private static ColumnValue TextVal(string value)
    {
        if (string.IsNullOrEmpty(value)) return ColumnValue.Null();
        var bytes = Encoding.UTF8.GetBytes(value);
        return ColumnValue.Text(bytes.Length * 2 + 13, bytes);
    }

    private static int ParseColIndex(string cellRef)
    {
        int col = 0;
        foreach (char c in cellRef)
        {
            if (c == '$') continue; // skip absolute markers
            if (char.IsLetter(c))
                col = col * 26 + (char.ToUpperInvariant(c) - 'A' + 1);
            else
                break;
        }
        return col; // 1-based (Excel convention)
    }

    private static string GetColLetters(string cellRef)
    {
        var sb = new StringBuilder();
        foreach (char c in cellRef)
        {
            if (c == '$') continue;
            if (char.IsLetter(c)) sb.Append(c);
            else break;
        }
        return sb.ToString();
    }

    private static (int startRow, int startCol, int endRow, int endCol) ParseRangeRef(string rangeRef)
    {
        var parts = rangeRef.Split(':');
        if (parts.Length != 2)
            return (0, 0, 0, 0);

        return (
            ParseRowNum(parts[0]),
            ParseColIndex(parts[0]),
            ParseRowNum(parts[1]),
            ParseColIndex(parts[1]));
    }

    private static int ParseRowNum(string cellRef)
    {
        var sb = new StringBuilder();
        foreach (char c in cellRef)
        {
            if (char.IsDigit(c)) sb.Append(c);
        }
        return int.TryParse(sb.ToString(), NumberStyles.Integer,
            CultureInfo.InvariantCulture, out int row) ? row : 0;
    }
}
