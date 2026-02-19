// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Core.Schema;

namespace Sharc.Views;

/// <summary>
/// Auto-converts simple SQLite views to native <see cref="SharcView"/> cursor factories.
/// A view qualifies when <see cref="ViewInfo.IsSharcExecutable"/> is true:
/// single source table, no JOIN, no WHERE, no subqueries.
/// Covers ~60-70% of real-world views.
/// </summary>
public static class ViewPromoter
{
    /// <summary>
    /// Attempts to create a <see cref="SharcView"/> from a SQLite VIEW definition.
    /// Returns null if the view's SQL is too complex for native execution.
    /// </summary>
    public static SharcView? TryPromote(ViewInfo viewInfo, SharcSchema schema)
    {
        if (!viewInfo.IsSharcExecutable)
            return null;

        string sourceTable = viewInfo.SourceTables[0];

        // Validate the source table exists
        try
        {
            schema.GetTable(sourceTable);
        }
        catch (KeyNotFoundException)
        {
            return null;
        }

        string[]? projectedColumns = null;
        if (!viewInfo.IsSelectAll && viewInfo.Columns.Count > 0)
        {
            projectedColumns = new string[viewInfo.Columns.Count];
            for (int i = 0; i < viewInfo.Columns.Count; i++)
                projectedColumns[i] = viewInfo.Columns[i].SourceName;
        }

        return new SharcView(
            viewInfo.Name,
            sourceTable,
            projectedColumns,
            filter: null);
    }
}
