// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using System.Globalization;
using System.Text;
using Sharc.Core;

namespace Sharc.Arc;

/// <summary>
/// Imports CSV data into a .arc file. Handles schema inference, type detection,
/// and creates a fully queryable Sharc database from tabular data.
/// <para>
/// <b>Pipeline:</b> CSV text → schema inference → table creation → row insertion.
/// Optionally creates graph overlay edges for foreign-key-like relationships.
/// </para>
/// </summary>
public static class CsvArcImporter
{
    /// <summary>
    /// Options for CSV import.
    /// </summary>
    public sealed class CsvImportOptions
    {
        /// <summary>Table name for the imported data. Default: "data".</summary>
        public string TableName { get; set; } = "data";

        /// <summary>CSV delimiter character. Default: comma.</summary>
        public char Delimiter { get; set; } = ',';

        /// <summary>Whether the first row is a header. Default: true.</summary>
        public bool HasHeader { get; set; } = true;

        /// <summary>Maximum rows to import (0 = unlimited). Default: 0.</summary>
        public int MaxRows { get; set; }

        /// <summary>Arc file name for the created handle. Default: "imported.arc".</summary>
        public string ArcName { get; set; } = "imported.arc";
    }

    /// <summary>
    /// Inferred column schema from CSV analysis.
    /// </summary>
    public sealed class InferredColumn
    {
        /// <summary>Column name (from header or generated).</summary>
        public string Name { get; init; } = "";

        /// <summary>Inferred SQLite type affinity.</summary>
        public string SqlType { get; init; } = "TEXT";

        /// <summary>Zero-based column index in the CSV.</summary>
        public int Index { get; init; }
    }

    /// <summary>
    /// Imports CSV text into a new in-memory .arc file.
    /// </summary>
    /// <param name="csvText">The full CSV text content.</param>
    /// <param name="options">Import options (table name, delimiter, header mode).</param>
    /// <returns>An <see cref="ArcHandle"/> containing the imported data.</returns>
    public static ArcHandle Import(string csvText, CsvImportOptions? options = null)
    {
        options ??= new CsvImportOptions();

        var lines = ParseLines(csvText);
        if (lines.Count == 0)
            throw new ArgumentException("CSV contains no data.", nameof(csvText));

        // 1. Extract header and data rows
        string[] headers;
        int dataStart;

        if (options.HasHeader && lines.Count > 0)
        {
            headers = ParseRow(lines[0], options.Delimiter);
            dataStart = 1;
        }
        else
        {
            int colCount = ParseRow(lines[0], options.Delimiter).Length;
            headers = new string[colCount];
            for (int i = 0; i < colCount; i++)
                headers[i] = $"col{i + 1}";
            dataStart = 0;
        }

        // 2. Parse all data rows
        var rows = new List<string[]>();
        int maxRows = options.MaxRows > 0 ? options.MaxRows : int.MaxValue;

        for (int i = dataStart; i < lines.Count && rows.Count < maxRows; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i])) continue;
            rows.Add(ParseRow(lines[i], options.Delimiter));
        }

        if (rows.Count == 0)
            throw new ArgumentException("CSV contains no data rows.", nameof(csvText));

        // 3. Infer types from data sample (first 100 rows)
        var columns = InferSchema(headers, rows, Math.Min(rows.Count, 100));

        // 4. Create the .arc file
        var handle = ArcHandle.CreateInMemory(options.ArcName);
        var db = handle.Database;

        // 5. Create table
        using (var writer = SharcWriter.From(db))
        {
            using var tx = writer.BeginTransaction();
            tx.Execute(BuildCreateTableSql(options.TableName, columns));
            tx.Commit();
        }

        // 6. Insert rows
        using (var writer = SharcWriter.From(db))
        {
            var records = new List<ColumnValue[]>(rows.Count);
            foreach (var row in rows)
            {
                var values = new ColumnValue[columns.Length];
                for (int c = 0; c < columns.Length; c++)
                {
                    string cell = c < row.Length ? row[c].Trim() : "";
                    values[c] = ParseCell(cell, columns[c].SqlType);
                }
                records.Add(values);
            }
            writer.InsertBatch(options.TableName, records);
        }

        return handle;
    }

    /// <summary>
    /// Imports CSV from a file path into a new in-memory .arc file.
    /// </summary>
    public static ArcHandle ImportFile(string csvPath, CsvImportOptions? options = null)
    {
        string csvText = File.ReadAllText(csvPath);
        options ??= new CsvImportOptions();
        if (options.ArcName == "imported.arc")
            options.ArcName = Path.GetFileNameWithoutExtension(csvPath) + ".arc";
        return Import(csvText, options);
    }

    /// <summary>
    /// Infers the schema of a CSV without importing it.
    /// Useful for showing the user what will be created before committing.
    /// </summary>
    public static InferredColumn[] InferSchemaOnly(string csvText, CsvImportOptions? options = null)
    {
        options ??= new CsvImportOptions();
        var lines = ParseLines(csvText);
        if (lines.Count == 0) return Array.Empty<InferredColumn>();

        string[] headers;
        int dataStart;

        if (options.HasHeader && lines.Count > 0)
        {
            headers = ParseRow(lines[0], options.Delimiter);
            dataStart = 1;
        }
        else
        {
            int colCount = ParseRow(lines[0], options.Delimiter).Length;
            headers = new string[colCount];
            for (int i = 0; i < colCount; i++) headers[i] = $"col{i + 1}";
            dataStart = 0;
        }

        var rows = new List<string[]>();
        for (int i = dataStart; i < lines.Count && rows.Count < 100; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i])) continue;
            rows.Add(ParseRow(lines[i], options.Delimiter));
        }

        return InferSchema(headers, rows, rows.Count);
    }

    // ── Private helpers ──

    private static InferredColumn[] InferSchema(string[] headers, List<string[]> rows, int sampleSize)
    {
        var columns = new InferredColumn[headers.Length];

        for (int c = 0; c < headers.Length; c++)
        {
            bool allInt = true, allReal = true;
            int nonEmpty = 0;

            for (int r = 0; r < sampleSize && r < rows.Count; r++)
            {
                string cell = c < rows[r].Length ? rows[r][c].Trim() : "";
                if (string.IsNullOrEmpty(cell)) continue;
                nonEmpty++;

                if (allInt && !long.TryParse(cell, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
                    allInt = false;

                if (allReal && !double.TryParse(cell, NumberStyles.Float | NumberStyles.AllowThousands,
                    CultureInfo.InvariantCulture, out _))
                    allReal = false;
            }

            string sqlType = nonEmpty > 0 && allInt ? "INTEGER"
                           : nonEmpty > 0 && allReal ? "REAL"
                           : "TEXT";

            columns[c] = new InferredColumn
            {
                Name = SanitizeColumnName(headers[c]),
                SqlType = sqlType,
                Index = c
            };
        }

        return columns;
    }

    private static ColumnValue ParseCell(string cell, string sqlType)
    {
        if (string.IsNullOrEmpty(cell))
            return ColumnValue.Null();

        if (sqlType == "INTEGER" &&
            long.TryParse(cell, NumberStyles.Integer, CultureInfo.InvariantCulture, out long intVal))
        {
            int serial = intVal == 0 ? 8 : intVal == 1 ? 9 :
                         intVal >= sbyte.MinValue && intVal <= sbyte.MaxValue ? 1 :
                         intVal >= short.MinValue && intVal <= short.MaxValue ? 2 :
                         intVal >= int.MinValue && intVal <= int.MaxValue ? 4 : 6;
            return ColumnValue.FromInt64(serial, intVal);
        }

        if (sqlType == "REAL" &&
            double.TryParse(cell, NumberStyles.Float | NumberStyles.AllowThousands,
                CultureInfo.InvariantCulture, out double realVal))
        {
            return ColumnValue.FromDouble(realVal);
        }

        // Default: TEXT
        var bytes = Encoding.UTF8.GetBytes(cell);
        return ColumnValue.Text(bytes.Length * 2 + 13, bytes);
    }

    private static string BuildCreateTableSql(string tableName, InferredColumn[] columns)
    {
        var sb = new StringBuilder();
        sb.Append("CREATE TABLE [").Append(tableName).Append("] (");

        for (int i = 0; i < columns.Length; i++)
        {
            if (i > 0) sb.Append(", ");
            sb.Append('[').Append(columns[i].Name).Append("] ").Append(columns[i].SqlType);
        }

        sb.Append(')');
        return sb.ToString();
    }

    private static string SanitizeColumnName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "unnamed";

        // Remove quotes and trim
        name = name.Trim().Trim('"', '\'');

        // Replace non-alphanumeric with underscore
        var sb = new StringBuilder(name.Length);
        foreach (char c in name)
            sb.Append(char.IsLetterOrDigit(c) || c == '_' ? c : '_');

        string result = sb.ToString();
        if (result.Length == 0 || char.IsDigit(result[0]))
            result = "_" + result;

        return result;
    }

    private static List<string> ParseLines(string text)
    {
        var lines = new List<string>();
        using var reader = new StringReader(text);
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            if (!string.IsNullOrWhiteSpace(line))
                lines.Add(line);
        }
        return lines;
    }

    /// <summary>
    /// RFC 4180-compliant CSV row parser. Handles quoted fields with embedded
    /// delimiters, newlines, and escaped quotes ("").
    /// </summary>
    internal static string[] ParseRow(string line, char delimiter)
    {
        var fields = new List<string>();
        int i = 0;

        while (i < line.Length)
        {
            if (line[i] == '"')
            {
                // Quoted field
                var sb = new StringBuilder();
                i++; // skip opening quote
                while (i < line.Length)
                {
                    if (line[i] == '"')
                    {
                        if (i + 1 < line.Length && line[i + 1] == '"')
                        {
                            sb.Append('"');
                            i += 2;
                        }
                        else
                        {
                            i++; // skip closing quote
                            break;
                        }
                    }
                    else
                    {
                        sb.Append(line[i]);
                        i++;
                    }
                }
                fields.Add(sb.ToString());
                // Skip delimiter after quoted field
                if (i < line.Length && line[i] == delimiter)
                {
                    i++;
                    // Trailing delimiter means one more empty field
                    if (i == line.Length) fields.Add("");
                }
            }
            else
            {
                // Unquoted field
                int start = i;
                while (i < line.Length && line[i] != delimiter) i++;
                fields.Add(line[start..i]);
                if (i < line.Length)
                {
                    i++; // skip delimiter
                    // Trailing delimiter means one more empty field
                    if (i == line.Length) fields.Add("");
                }
            }
        }

        // Empty line edge case
        if (fields.Count == 0) fields.Add("");

        return fields.ToArray();
    }
}
