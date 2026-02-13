using System.ComponentModel;
using System.Text;
using ModelContextProtocol.Server;

namespace Sharc.Context.Tools;

/// <summary>
/// MCP tools for querying SQLite databases using the Sharc library.
/// </summary>
[McpServerToolType]
public static class ContextQueryTool
{
    private const int MaxRows = 100;

    /// <summary>
    /// Lists all tables and columns in a SQLite database file.
    /// </summary>
    [McpServerTool, Description(
        "List all tables and columns in a SQLite database file. " +
        "Returns table names, column names, types, and constraints as markdown.")]
    public static string ListSchema(
        [Description("Absolute path to the SQLite .db file")]
        string databasePath)
    {
        try
        {
            if (!File.Exists(databasePath))
                return $"Error: File not found: {databasePath}";

            using var db = SharcDatabase.Open(databasePath);
            var sb = new StringBuilder();
            sb.AppendLine($"# Schema: {Path.GetFileName(databasePath)}");
            sb.AppendLine();

            var tables = db.Schema.Tables;
            if (tables.Count == 0)
            {
                sb.AppendLine("No tables found.");
                return sb.ToString();
            }

            foreach (var table in tables)
            {
                sb.AppendLine($"## {table.Name}");
                sb.AppendLine();
                sb.AppendLine("| Column | Type | PK | NOT NULL |");
                sb.AppendLine("|--------|------|----|----------|");
                foreach (var col in table.Columns)
                {
                    var pk = col.IsPrimaryKey ? "YES" : "";
                    var notNull = col.IsNotNull ? "YES" : "";
                    sb.AppendLine($"| {col.Name} | {col.DeclaredType} | {pk} | {notNull} |");
                }
                sb.AppendLine();
            }

            return sb.ToString();
        }
        catch (Exception ex)
        {
            return $"Error reading database: {ex.Message}";
        }
    }

    /// <summary>
    /// Gets the total row count for a specific table.
    /// </summary>
    [McpServerTool, Description(
        "Get the total row count for a specific table in a SQLite database.")]
    public static string GetRowCount(
        [Description("Absolute path to the SQLite .db file")]
        string databasePath,
        [Description("Name of the table to count")]
        string tableName)
    {
        try
        {
            if (!File.Exists(databasePath))
                return $"Error: File not found: {databasePath}";

            using var db = SharcDatabase.Open(databasePath);
            var count = db.GetRowCount(tableName);
            return $"{tableName}: {count} rows";
        }
        catch (KeyNotFoundException)
        {
            return $"Error: Table '{tableName}' not found.";
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }
    }

    /// <summary>
    /// Queries a table and returns rows as a markdown table.
    /// </summary>
    [McpServerTool, Description(
        "Query a table in a SQLite database. Returns up to 100 rows as a markdown table. " +
        "Supports optional column projection and simple equality filters.")]
    public static string QueryTable(
        [Description("Absolute path to the SQLite .db file")]
        string databasePath,
        [Description("Name of the table to query")]
        string tableName,
        [Description("Comma-separated column names for projection (empty = all columns)")]
        string? columns = null,
        [Description("Simple filter in 'column=value' format (empty = no filter)")]
        string? filter = null)
    {
        try
        {
            if (!File.Exists(databasePath))
                return $"Error: File not found: {databasePath}";

            using var db = SharcDatabase.Open(databasePath);

            string[]? columnArray = null;
            if (!string.IsNullOrWhiteSpace(columns))
                columnArray = columns.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            SharcFilter[]? filters = null;
            if (!string.IsNullOrWhiteSpace(filter))
            {
                filters = ParseFilters(filter);
                if (filters is null)
                    return $"Error: Invalid filter format '{filter}'. Use 'column=value'.";
            }

            using var reader = (columnArray, filters) switch
            {
                (not null, not null) => db.CreateReader(tableName, columnArray, filters),
                (not null, null) => db.CreateReader(tableName, columnArray),
                (null, not null) => db.CreateReader(tableName, filters),
                _ => db.CreateReader(tableName)
            };

            var sb = new StringBuilder();
            var fieldCount = reader.FieldCount;

            // Header
            var headerNames = new string[fieldCount];
            for (int i = 0; i < fieldCount; i++)
                headerNames[i] = reader.GetColumnName(i);

            sb.AppendLine("| " + string.Join(" | ", headerNames) + " |");
            sb.AppendLine("| " + string.Join(" | ", headerNames.Select(_ => "---")) + " |");

            int rowCount = 0;
            while (reader.Read() && rowCount < MaxRows)
            {
                var values = new string[fieldCount];
                for (int i = 0; i < fieldCount; i++)
                    values[i] = reader.IsNull(i) ? "NULL" : (reader.GetValue(i)?.ToString() ?? "NULL");

                sb.AppendLine("| " + string.Join(" | ", values) + " |");
                rowCount++;
            }

            bool hasMore = reader.Read();

            sb.Insert(0, $"**{rowCount} row(s)** from `{tableName}`\n\n");

            if (hasMore)
                sb.AppendLine($"\n*... truncated at {MaxRows} rows*");

            return sb.ToString();
        }
        catch (KeyNotFoundException)
        {
            return $"Error: Table '{tableName}' not found.";
        }
        catch (ArgumentException ex)
        {
            return $"Error: {ex.Message}";
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }
    }

    /// <summary>
    /// Seeks to a specific row by its rowid.
    /// </summary>
    [McpServerTool, Description(
        "Seek to a specific row by its rowid in a SQLite table. " +
        "Returns the single row as a markdown table, or 'not found'.")]
    public static string SeekRow(
        [Description("Absolute path to the SQLite .db file")]
        string databasePath,
        [Description("Name of the table")]
        string tableName,
        [Description("The rowid to seek to")]
        long rowId)
    {
        try
        {
            if (!File.Exists(databasePath))
                return $"Error: File not found: {databasePath}";

            using var db = SharcDatabase.Open(databasePath);
            using var reader = db.CreateReader(tableName);

            if (!reader.Seek(rowId))
                return $"Row with rowid {rowId} not found in table '{tableName}'.";

            var sb = new StringBuilder();
            var fieldCount = reader.FieldCount;

            var headerNames = new string[fieldCount];
            for (int i = 0; i < fieldCount; i++)
                headerNames[i] = reader.GetColumnName(i);

            sb.AppendLine("| " + string.Join(" | ", headerNames) + " |");
            sb.AppendLine("| " + string.Join(" | ", headerNames.Select(_ => "---")) + " |");

            var values = new string[fieldCount];
            for (int i = 0; i < fieldCount; i++)
                values[i] = reader.IsNull(i) ? "NULL" : (reader.GetValue(i)?.ToString() ?? "NULL");

            sb.AppendLine("| " + string.Join(" | ", values) + " |");
            return sb.ToString();
        }
        catch (KeyNotFoundException)
        {
            return $"Error: Table '{tableName}' not found.";
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }
    }

    private static SharcFilter[]? ParseFilters(string filter)
    {
        var parts = filter.Split('=', 2);
        if (parts.Length != 2)
            return null;

        var columnName = parts[0].Trim();
        var value = parts[1].Trim();

        if (string.IsNullOrEmpty(columnName))
            return null;

        // Try parsing as number first
        object filterValue;
        if (long.TryParse(value, out var longVal))
            filterValue = longVal;
        else if (double.TryParse(value, out var doubleVal))
            filterValue = doubleVal;
        else
            filterValue = value;

        return [new SharcFilter(columnName, SharcOperator.Equal, filterValue)];
    }
}
