// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Query.Intent;

namespace Sharc.Query;

/// <summary>
/// Collects all table references from a <see cref="QueryPlan"/> for entitlement enforcement.
/// </summary>
internal static class TableReferenceCollector
{
    internal static List<(string table, string[]? columns)> Collect(QueryPlan plan)
    {
        var tables = new List<(string, string[]?)>();

        if (plan.HasCotes)
        {
            foreach (var cote in plan.Cotes!)
                tables.Add((cote.Query.TableName, cote.Query.ColumnsArray));
        }

        if (plan.IsCompound)
            CollectFromCompound(plan.Compound!, tables);
        else if (plan.Simple is not null)
            tables.Add((plan.Simple.TableName, plan.Simple.ColumnsArray));

        return tables;
    }

    private static void CollectFromCompound(CompoundQueryPlan plan, List<(string, string[]?)> tables)
    {
        tables.Add((plan.Left.TableName, plan.Left.ColumnsArray));

        if (plan.RightCompound != null)
            CollectFromCompound(plan.RightCompound, tables);
        else if (plan.RightSimple != null)
            tables.Add((plan.RightSimple.TableName, plan.RightSimple.ColumnsArray));
    }
}
