// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Query.Intent;

namespace Sharc.Query;

/// <summary>
/// Collects all table references from a <see cref="QueryPlan"/> for entitlement enforcement.
/// </summary>
internal static class TableReferenceCollector
{
    internal static List<TableReference> Collect(QueryPlan plan)
    {
        var tables = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        if (plan.HasCotes)
        {
            foreach (var cote in plan.Cotes!)
                Merge(tables, Collect(cote.Query));
        }

        if (plan.IsCompound)
            CollectFromCompound(plan.Compound!, tables);
        else if (plan.Simple is not null)
            CollectFromIntent(plan.Simple, tables);

        return tables.Select(kv => new TableReference(kv.Key, kv.Value.ToArray())).ToList();
    }

    private static void Merge(Dictionary<string, HashSet<string>> target, List<TableReference> source)
    {
        foreach (var (table, columns) in source)
        {
            if (!target.TryGetValue(table, out var columnSet))
                target[table] = columnSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (columns != null)
            {
                foreach (var col in columns)
                    columnSet.Add(col);
            }
        }
    }

    private static void CollectFromCompound(CompoundQueryPlan plan, Dictionary<string, HashSet<string>> tables)
    {
        CollectFromIntent(plan.Left, tables);

        if (plan.RightCompound != null)
            CollectFromCompound(plan.RightCompound, tables);
        else if (plan.RightSimple != null)
            CollectFromIntent(plan.RightSimple, tables);
    }

    private static void CollectFromIntent(QueryIntent intent, Dictionary<string, HashSet<string>> tables)
    {
        var primaryTable = intent.TableName;
        var primaryAlias = intent.TableAlias;
        
        void RecordColumn(string? rawColumn)
        {
            if (string.IsNullOrEmpty(rawColumn)) return;

            string table = primaryTable;
            string column = rawColumn;

            int dotIdx = rawColumn.IndexOf('.');
            if (dotIdx > 0)
            {
                string alias = rawColumn[..dotIdx];
                column = rawColumn[(dotIdx + 1)..];

                if (string.Equals(alias, primaryAlias, StringComparison.OrdinalIgnoreCase))
                {
                    table = primaryTable;
                }
                else if (intent.Joins != null)
                {
                    foreach (var join in intent.Joins)
                    {
                        if (string.Equals(alias, join.TableAlias, StringComparison.OrdinalIgnoreCase))
                        {
                            table = join.TableName;
                            break;
                        }
                    }
                }
            }

            if (!tables.TryGetValue(table, out var columnSet))
                tables[table] = columnSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            
            columnSet.Add(column);
        }

        // 1. Projected columns
        if (intent.Columns != null)
        {
            foreach (var col in intent.Columns) RecordColumn(col);
        }
        else
        {
            // SELECT * - ensures the table is at least registered
            if (!tables.ContainsKey(primaryTable))
                tables[primaryTable] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        // 2. Filter (WHERE)
        if (intent.Filter != null)
        {
            foreach (var node in intent.Filter.Value.Nodes)
                RecordColumn(node.ColumnName);
        }

        // 3. Joins (ON clauses)
        if (intent.Joins != null)
        {
            foreach (var join in intent.Joins)
            {
                RecordColumn(join.LeftColumn);
                RecordColumn(join.RightColumn);
                
                // Ensure the joined table is registered even if no columns are referenced in ON
                if (!tables.ContainsKey(join.TableName))
                    tables[join.TableName] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }
        }

        // 4. Order By
        if (intent.OrderBy != null)
        {
            foreach (var order in intent.OrderBy) RecordColumn(order.ColumnName);
        }

        // 5. Group By
        if (intent.GroupBy != null)
        {
            foreach (var col in intent.GroupBy) RecordColumn(col);
        }

        // 6. Having
        if (intent.HavingFilter != null)
        {
            foreach (var node in intent.HavingFilter.Value.Nodes)
                RecordColumn(node.ColumnName);
        }
    }
}
