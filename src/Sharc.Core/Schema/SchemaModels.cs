/*-------------------------------------------------------------------------------------------------!
  "Where the mind is free to imagine and the craft is guided by clarity, code awakens."            |

  A collaborative work shaped by Artificial Intelligence and curated with intent by Ram Revanur.
  Software here is treated not as static text, but as a living system designed to learn and evolve.
  Built on the belief that architecture and context often define outcomes before code is written.

  This file reflects an AI-aware, agentic, context-driven, and continuously evolving approach
  to modern engineering. If you seek to transform a traditional codebase into an adaptive,
  intelligence-guided system, you may find resonance in these patterns and principles.

  Subtle conversations often begin with a single message Ã¢â‚¬â€ or a prompt with the right context.
  https://www.linkedin.com/in/revodoc/

  Licensed under the MIT License Ã¢â‚¬â€ free for personal and commercial use.                           |
--------------------------------------------------------------------------------------------------*/

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
    public TableInfo GetTable(string name) =>
        Tables.FirstOrDefault(t => t.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
        ?? throw new KeyNotFoundException($"Table '{name}' not found in schema.");
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

    /// <summary>Whether this is a WITHOUT ROWID table.</summary>
    public required bool IsWithoutRowId { get; init; }
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
/// </summary>
public sealed class ViewInfo
{
    /// <summary>View name.</summary>
    public required string Name { get; init; }

    /// <summary>Original CREATE VIEW SQL statement.</summary>
    public required string Sql { get; init; }
}
