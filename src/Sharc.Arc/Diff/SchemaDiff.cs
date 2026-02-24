// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

namespace Sharc.Arc.Diff;

/// <summary>
/// Schema-level diff between two arcs: tables added, removed, or modified.
/// </summary>
public sealed class SchemaDiff
{
    /// <summary>True if both arcs have identical schemas.</summary>
    public bool IsIdentical => TablesOnlyInLeft.Count == 0
        && TablesOnlyInRight.Count == 0
        && ModifiedTables.Count == 0;

    /// <summary>Tables present in left arc but not in right.</summary>
    public required IReadOnlyList<string> TablesOnlyInLeft { get; init; }

    /// <summary>Tables present in right arc but not in left.</summary>
    public required IReadOnlyList<string> TablesOnlyInRight { get; init; }

    /// <summary>Tables present in both but with structural differences.</summary>
    public required IReadOnlyList<TableSchemaDiff> ModifiedTables { get; init; }

    /// <summary>Tables present in both with identical schema.</summary>
    public required IReadOnlyList<string> CommonTables { get; init; }
}

/// <summary>
/// Column-level schema differences for a single table present in both arcs.
/// </summary>
public sealed class TableSchemaDiff
{
    /// <summary>Name of the table.</summary>
    public required string TableName { get; init; }

    /// <summary>Columns in the left arc but not the right.</summary>
    public required IReadOnlyList<string> ColumnsOnlyInLeft { get; init; }

    /// <summary>Columns in the right arc but not the left.</summary>
    public required IReadOnlyList<string> ColumnsOnlyInRight { get; init; }

    /// <summary>Columns with type changes between arcs.</summary>
    public required IReadOnlyList<ColumnTypeChange> TypeChanges { get; init; }
}

/// <summary>
/// Describes a column type change between two arcs.
/// </summary>
public sealed class ColumnTypeChange
{
    /// <summary>Column name.</summary>
    public required string ColumnName { get; init; }

    /// <summary>Declared type in the left arc.</summary>
    public required string LeftType { get; init; }

    /// <summary>Declared type in the right arc.</summary>
    public required string RightType { get; init; }
}
