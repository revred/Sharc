// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.


namespace Sharc.Core.Schema;

/// <summary>
/// Represents the complete schema of a SQLite database.
/// </summary>
public sealed class SharcSchema
{
    /// <summary>All tables in the database.</summary>
    public required IReadOnlyList<TableInfo> Tables { get; init; }

    /// <summary>All indexes in the database.</summary>
    public required IReadOnlyList<IndexInfo> Indexes { get; init; }

    /// <summary>All views in the database.</summary>
    public required IReadOnlyList<ViewInfo> Views { get; init; }

    /// <summary>
    /// Gets a table by name (case-insensitive).
    /// </summary>
    /// <exception cref="KeyNotFoundException">Table not found.</exception>
    public TableInfo GetTable(string name)
    {
        // Zero-allocation loop instead of LINQ
        var count = Tables.Count;
        for (int i = 0; i < count; i++)
        {
            if (Tables[i].Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                return Tables[i];
        }
        throw new KeyNotFoundException($"Table '{name}' not found in schema.");
    }

    /// <summary>
    /// Gets a view by name (case-insensitive).
    /// </summary>
    /// <returns>Null if not found.</returns>
    public ViewInfo? GetView(string name)
    {
        var count = Views.Count;
        for (int i = 0; i < count; i++)
        {
            if (Views[i].Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                return Views[i];
        }
        return null;
    }
}

/// <summary>
/// Metadata for a single table.
/// </summary>
public sealed class TableInfo
{
    /// <summary>Table name.</summary>
    public required string Name { get; init; }

    /// <summary>Root b-tree page number.</summary>
    public required int RootPage { get; init; }

    /// <summary>Original CREATE TABLE SQL statement.</summary>
    public required string Sql { get; init; }

    /// <summary>Columns in declaration order.</summary>
    public required IReadOnlyList<ColumnInfo> Columns { get; init; }

    /// <summary>
    /// Indexes associated with this table.
    /// populated by SchemaReader.
    /// </summary>
    public IReadOnlyList<IndexInfo> Indexes { get; internal set; } = [];

    /// <summary>Whether this is a WITHOUT ROWID table.</summary>
    public required bool IsWithoutRowId { get; init; }

    /// <summary>
    /// Physical column count on disk. Equals Columns.Count when no merged columns exist.
    /// Greater than Columns.Count when __hi/__lo pairs are merged into logical GUID columns.
    /// </summary>
    public int PhysicalColumnCount { get; internal set; }

    /// <summary>True if any column is a merged column (physical count exceeds logical count).</summary>
    public bool HasMergedColumns => PhysicalColumnCount > Columns.Count;

    private Dictionary<string, int>? _columnMap;

    /// <summary>
    /// Gets the ordinal of a column by name (case-insensitive).
    /// </summary>
    public int GetColumnOrdinal(string name)
    {
        _columnMap ??= Columns.ToDictionary(c => c.Name, c => c.Ordinal, StringComparer.OrdinalIgnoreCase);
        return _columnMap.TryGetValue(name, out int ordinal) ? ordinal : -1;
    }
}

/// <summary>
/// Metadata for a single column.
/// </summary>
public sealed class ColumnInfo
{
    /// <summary>Column name.</summary>
    public required string Name { get; init; }

    /// <summary>Declared type string (e.g., "INTEGER", "TEXT", "VARCHAR(255)").</summary>
    public required string DeclaredType { get; init; }

    /// <summary>Column ordinal position (0-based).</summary>
    public required int Ordinal { get; init; }

    /// <summary>Whether the column is part of the primary key.</summary>
    public required bool IsPrimaryKey { get; init; }

    /// <summary>Whether the column has a NOT NULL constraint.</summary>
    public required bool IsNotNull { get; init; }

    /// <summary>Whether the declared type is GUID or UUID, indicating a 16-byte unique identifier.</summary>
    public bool IsGuidColumn { get; init; }

    /// <summary>
    /// Physical ordinals of the __hi and __lo columns when this is a merged GUID column.
    /// Null for regular (non-merged) columns.
    /// </summary>
    public int[]? MergedPhysicalOrdinals { get; init; }

    /// <summary>True if this is a merged GUID column backed by two physical Int64 columns.</summary>
    public bool IsMergedGuidColumn => MergedPhysicalOrdinals is { Length: 2 };
}

/// <summary>
/// Metadata for a single index.
/// </summary>
public sealed class IndexInfo
{
    /// <summary>Index name.</summary>
    public required string Name { get; init; }

    /// <summary>Table this index belongs to.</summary>
    public required string TableName { get; init; }

    /// <summary>Root b-tree page number.</summary>
    public required int RootPage { get; init; }

    /// <summary>Original CREATE INDEX SQL statement.</summary>
    public required string Sql { get; init; }

    /// <summary>Whether this is a UNIQUE index.</summary>
    public required bool IsUnique { get; init; }

    /// <summary>Columns in the index, in index key order.</summary>
    public required IReadOnlyList<IndexColumnInfo> Columns { get; init; }
}

/// <summary>
/// Metadata for a single column in an index.
/// </summary>
public sealed class IndexColumnInfo
{
    /// <summary>Column name.</summary>
    public required string Name { get; init; }

    /// <summary>Position in the index key (0-based).</summary>
    public required int Ordinal { get; init; }

    /// <summary>Whether this column is sorted in descending order.</summary>
    public required bool IsDescending { get; init; }
}

/// <summary>
/// Metadata for a single view.
/// Extended with structural metadata parsed from CREATE VIEW SQL.
/// </summary>
public sealed class ViewInfo
{
    /// <summary>View name.</summary>
    public required string Name { get; init; }

    /// <summary>Original CREATE VIEW SQL statement.</summary>
    public required string Sql { get; init; }

    /// <summary>Source table name(s) referenced in FROM clause.</summary>
    public IReadOnlyList<string> SourceTables { get; init; } = [];

    /// <summary>Column names/aliases in the SELECT list. Empty if SELECT *.</summary>
    public IReadOnlyList<ViewColumnInfo> Columns { get; init; } = [];

    /// <summary>True if the view uses SELECT *.</summary>
    public bool IsSelectAll { get; init; }

    /// <summary>True if the view's SQL contains JOIN.</summary>
    public bool HasJoin { get; init; }

    /// <summary>True if the view's SQL contains WHERE.</summary>
    public bool HasFilter { get; init; }

    /// <summary>True if structural metadata was successfully parsed from the SQL.</summary>
    public bool ParseSucceeded { get; init; }

    /// <summary>
    /// Whether Sharc can natively execute this view as a zero-allocation cursor.
    /// True when: single source table, no JOIN, no WHERE, no subqueries.
    /// These views can be auto-promoted to a SharcView.
    /// </summary>
    public bool IsSharcExecutable =>
        ParseSucceeded && SourceTables.Count == 1 && !HasJoin && !HasFilter;
}