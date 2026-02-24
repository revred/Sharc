// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Query.Intent;

namespace Sharc.Query;

/// <summary>
/// Computes the physical column set needed when CASE expressions are present.
/// Replaces CASE alias columns with their source columns so the reader fetches
/// the actual table columns that the CASE evaluator needs.
/// </summary>
internal static class CaseProjection
{
    /// <summary>
    /// Returns the physical columns to fetch from the table, and a mapping
    /// from the final output column names to their physical positions.
    /// </summary>
    internal static string[] Compute(QueryIntent intent)
    {
        var physicalColumns = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var caseOrdinals = new HashSet<int>();

        // Collect CASE expression output ordinals
        if (intent.CaseExpressions is { Count: > 0 })
        {
            foreach (var ce in intent.CaseExpressions)
                caseOrdinals.Add(ce.OutputOrdinal);
        }

        // Add non-CASE columns from the SELECT list
        if (intent.Columns is { Count: > 0 })
        {
            for (int i = 0; i < intent.Columns.Count; i++)
            {
                if (caseOrdinals.Contains(i))
                    continue; // Skip CASE alias — not a physical column
                string col = intent.Columns[i];
                if (seen.Add(col))
                    physicalColumns.Add(col);
            }
        }

        // Add source columns from all CASE expressions
        if (intent.CaseExpressions is { Count: > 0 })
        {
            foreach (var ce in intent.CaseExpressions)
            {
                foreach (var src in ce.SourceColumns)
                {
                    if (seen.Add(src))
                        physicalColumns.Add(src);
                }
            }
        }

        // Add ORDER BY columns (may reference columns not in SELECT)
        if (intent.OrderBy is { Count: > 0 })
        {
            foreach (var order in intent.OrderBy)
            {
                if (seen.Add(order.ColumnName))
                    physicalColumns.Add(order.ColumnName);
            }
        }

        return physicalColumns.ToArray();
    }
}
