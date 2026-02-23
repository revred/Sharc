// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Core.Schema;

namespace Sharc.Views;

/// <summary>
/// A named, reusable, immutable cursor configuration — a pre-compiled read lens.
/// Opening a view returns a forward-only <see cref="IViewCursor"/> over the
/// projected columns. Views are zero-allocation after construction.
/// <para>
/// A view can source from a table (<see cref="SourceTable"/>) or from another
/// view (<see cref="SourceView"/>), enabling composable view chains (subviews).
/// </para>
/// <para>
/// Construct via <see cref="ViewBuilder"/> for a fluent API,
/// or via <see cref="ViewPromoter"/> for automatic SQLite view conversion.
/// </para>
/// </summary>
public sealed class SharcView : ILayer
{
    /// <summary>Human-readable name for this view.</summary>
    public string Name { get; }

    /// <summary>
    /// The table this view reads from, or null if sourced from another view.
    /// Exactly one of <see cref="SourceTable"/> and <see cref="SourceView"/> is non-null.
    /// </summary>
    public string? SourceTable { get; }

    /// <summary>
    /// The parent view this view reads from, or null if sourced from a table.
    /// Exactly one of <see cref="SourceTable"/> and <see cref="SourceView"/> is non-null.
    /// </summary>
    public SharcView? SourceView { get; }

    /// <summary>
    /// Column names to project. If null, all columns are returned.
    /// </summary>
    public IReadOnlyList<string>? ProjectedColumnNames { get; }

    /// <summary>
    /// Optional row filter predicate. Applied during cursor iteration.
    /// Signature: (IRowAccessor row) -> bool (true = include).
    /// </summary>
    public Func<IRowAccessor, bool>? Filter { get; }

    /// <summary>
    /// Controls how rows are produced during cursor iteration.
    /// Default is <see cref="MaterializationStrategy.Eager"/>.
    /// </summary>
    public MaterializationStrategy Strategy { get; }

    // Cached array form of ProjectedColumnNames — avoids per-Open .ToArray() allocation.
    private readonly string[]? _projectedColumnArray;

    /// <summary>Creates a table-sourced view.</summary>
    internal SharcView(string name, string sourceTable, IReadOnlyList<string>? projectedColumnNames,
        Func<IRowAccessor, bool>? filter, MaterializationStrategy strategy = MaterializationStrategy.Eager)
    {
        Name = name;
        SourceTable = sourceTable;
        ProjectedColumnNames = projectedColumnNames;
        _projectedColumnArray = projectedColumnNames?.ToArray();
        Filter = filter;
        Strategy = strategy;
    }

    /// <summary>Creates a view-sourced view (subview).</summary>
    internal SharcView(string name, SharcView sourceView, IReadOnlyList<string>? projectedColumnNames,
        Func<IRowAccessor, bool>? filter, MaterializationStrategy strategy = MaterializationStrategy.Eager)
    {
        Name = name;
        SourceView = sourceView;
        ProjectedColumnNames = projectedColumnNames;
        _projectedColumnArray = projectedColumnNames?.ToArray();
        Filter = filter;
        Strategy = strategy;
    }

    /// <summary>Maximum depth for subview chains to prevent stack overflow from circular references.</summary>
    private const int MaxSubviewDepth = 10;

    /// <summary>
    /// Opens this view against the given database and returns a cursor.
    /// The cursor respects column projection and optional filtering.
    /// For subviews, recursively opens the parent view's cursor.
    /// </summary>
    /// <param name="db">The database to read from.</param>
    /// <returns>A forward-only cursor over the view's projected rows.</returns>
    /// <exception cref="InvalidOperationException">Circular subview dependency or chain depth exceeded.</exception>
    public IViewCursor Open(SharcDatabase db)
    {
        if (SourceView != null)
        {
            // Validate subview chain: detect cycles and enforce depth limit
            var visited = new HashSet<SharcView>(ReferenceEqualityComparer.Instance);
            var current = this;
            while (current != null)
            {
                if (!visited.Add(current))
                    throw new InvalidOperationException(
                        $"Circular subview dependency detected: view '{current.Name}' appears twice in the chain.");
                if (visited.Count > MaxSubviewDepth)
                    throw new InvalidOperationException(
                        $"Subview chain depth exceeded limit ({MaxSubviewDepth}). " +
                        $"View '{Name}' has too many nested parent views.");
                current = current.SourceView;
            }

            return OpenFromView(db);
        }

        return OpenFromTable(db);
    }

    private IViewCursor OpenFromTable(SharcDatabase db)
    {
        // Create a reader for the source table with column projection
        var reader = _projectedColumnArray != null
            ? db.CreateReader(SourceTable!, _projectedColumnArray)
            : db.CreateReader(SourceTable!);

        // Build column name array for the cursor
        var columnNames = reader.GetColumnNames();

        // No ordinal remapping needed — projection is handled by CreateReader
        IViewCursor cursor = new SimpleViewCursor(reader, projection: null, columnNames);

        // Apply filter decorator if present
        if (Filter != null)
            cursor = new FilteredViewCursor(cursor, Filter);

        return cursor;
    }

    private IViewCursor OpenFromView(SharcDatabase db)
    {
        // Recursively open the parent view
        var parentCursor = SourceView!.Open(db);

        // Build projection mapping: subviewOrdinal → parentOrdinal
        int[]? projection = null;
        string[] columnNames;

        if (ProjectedColumnNames != null)
        {
            // Build a lookup from parent column name → parent ordinal
            var parentColumnCount = parentCursor.FieldCount;
            projection = new int[ProjectedColumnNames.Count];
            columnNames = new string[ProjectedColumnNames.Count];

            for (int i = 0; i < ProjectedColumnNames.Count; i++)
            {
                string wanted = ProjectedColumnNames[i];
                int found = -1;
                for (int p = 0; p < parentColumnCount; p++)
                {
                    if (string.Equals(parentCursor.GetColumnName(p), wanted, StringComparison.OrdinalIgnoreCase))
                    {
                        found = p;
                        break;
                    }
                }
                if (found < 0)
                    throw new ArgumentException($"Column '{wanted}' not found in parent view '{SourceView.Name}'.");

                projection[i] = found;
                columnNames[i] = wanted;
            }
        }
        else
        {
            // All columns from parent
            columnNames = new string[parentCursor.FieldCount];
            for (int i = 0; i < columnNames.Length; i++)
                columnNames[i] = parentCursor.GetColumnName(i);
        }

        IViewCursor cursor = new ProjectedViewCursor(parentCursor, projection, columnNames);

        // Apply filter decorator if present
        if (Filter != null)
            cursor = new FilteredViewCursor(cursor, Filter);

        return cursor;
    }
}
