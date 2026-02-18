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
        var tables = new List<TableReference>();

        if (plan.HasCotes)
        {
            foreach (var cote in plan.Cotes!)
                tables.AddRange(Collect(cote.Query));
        }

        if (plan.IsCompound)
            CollectFromCompound(plan.Compound!, tables);
        else if (plan.Simple is not null)
            CollectFromIntent(plan.Simple, tables);

        return tables;
    }

    private static void CollectFromCompound(CompoundQueryPlan plan, List<TableReference> tables)
    {
        CollectFromIntent(plan.Left, tables);

        if (plan.RightCompound != null)
            CollectFromCompound(plan.RightCompound, tables);
        else if (plan.RightSimple != null)
            CollectFromIntent(plan.RightSimple, tables);
    }

    private static void CollectFromIntent(QueryIntent intent, List<TableReference> tables)
    {
        tables.Add(new TableReference(intent.TableName, intent.ColumnsArray));

        if (intent.Joins != null)
        {
            foreach (var join in intent.Joins)
            {
                // For joined tables, we don't easily know which columns are accessed without deeper analysis
                // But for entitlement, we usually just need to know the table is accessed.
                // If column-level security is required, we'd need to map columns to tables.
                // For now, let's assume * (all columns) or just the table access check.
                // The EntitlementEnforcer checks (Table, Columns). 
                // Passing null columns means "Any access to table".
                // Ideally we'd parse the columns to see which table they belong to.
                // But Joins might not even select columns, just filter.
                tables.Add(new TableReference(join.TableName, null));
            }
        }
    }
}
