// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Query.Intent;

namespace Sharc.Query;

/// <summary>
/// Streaming execution for UNION ALL queries. Avoids full materialization when possible:
/// either zero-copy concatenation of two cursor-mode readers, or streaming TopN heap.
/// </summary>
internal static class StreamingUnionExecutor
{
    /// <summary>
    /// Returns true when a compound plan can use zero-materialization streaming:
    /// UNION ALL, no final ORDER BY / LIMIT / OFFSET, simple two-way, no Cote references.
    /// </summary>
    internal static bool CanStreamUnionAll(
        CompoundQueryPlan plan,
        CoteMap? coteResults)
    {
        if (plan.Operator != CompoundOperator.UnionAll) return false;
        if (plan.FinalOrderBy is { Count: > 0 }) return false;
        if (plan.FinalLimit.HasValue || plan.FinalOffset.HasValue) return false;
        if (plan.RightCompound != null) return false;
        if (plan.RightSimple == null) return false;

        if (coteResults != null)
        {
            if (coteResults.ContainsKey(plan.Left.TableName)) return false;
            if (coteResults.ContainsKey(plan.RightSimple.TableName)) return false;
        }

        return true;
    }

    /// <summary>
    /// Executes a two-way UNION ALL by concatenating two cursor-mode readers.
    /// Zero materialization — rows stream directly from the underlying B-tree cursors.
    /// </summary>
    internal static SharcDataReader StreamingUnionAll(
        SharcDatabase db,
        CompoundQueryPlan plan,
        IReadOnlyDictionary<string, object>? parameters)
    {
        var leftReader = ExecuteIntentAsReader(db, plan.Left, parameters);
        var rightReader = ExecuteIntentAsReader(db, plan.RightSimple!, parameters);

        var columnNames = leftReader.GetColumnNames();
        return new SharcDataReader(leftReader, rightReader, columnNames);
    }

    /// <summary>
    /// Returns true when a compound plan can use streaming UNION ALL + TopN:
    /// UNION ALL with ORDER BY + LIMIT (no OFFSET), simple two-way, no Cote references.
    /// </summary>
    internal static bool CanStreamUnionAllTopN(
        CompoundQueryPlan plan,
        CoteMap? coteResults)
    {
        if (plan.Operator != CompoundOperator.UnionAll) return false;
        if (plan.FinalOrderBy is not { Count: > 0 }) return false;
        if (!plan.FinalLimit.HasValue) return false;
        if (plan.RightCompound != null) return false;
        if (plan.RightSimple == null) return false;

        if (coteResults != null)
        {
            if (coteResults.ContainsKey(plan.Left.TableName)) return false;
            if (coteResults.ContainsKey(plan.RightSimple.TableName)) return false;
        }

        return true;
    }

    /// <summary>
    /// Executes UNION ALL + ORDER BY + LIMIT by concatenating two readers
    /// and feeding through the streaming TopN heap with fast rejection.
    /// </summary>
    internal static SharcDataReader StreamingUnionAllTopN(
        SharcDatabase db,
        CompoundQueryPlan plan,
        IReadOnlyDictionary<string, object>? parameters)
    {
        var leftReader = ExecuteIntentAsReader(db, plan.Left, parameters);
        var rightReader = ExecuteIntentAsReader(db, plan.RightSimple!, parameters);

        var columnNames = leftReader.GetColumnNames();
        var concatReader = new SharcDataReader(leftReader, rightReader, columnNames);
        return StreamingTopNProcessor.Apply(
            concatReader, plan.FinalOrderBy!, plan.FinalLimit!.Value, plan.FinalOffset ?? 0);
    }

    /// <summary>
    /// Executes a single <see cref="QueryIntent"/> and returns the reader directly
    /// (with post-processing applied).
    /// </summary>
    internal static SharcDataReader ExecuteIntentAsReader(
        SharcDatabase db,
        QueryIntent intent,
        IReadOnlyDictionary<string, object>? parameters)
    {
        var reader = db.CreateReaderFromIntent(intent, parameters);
        return QueryPostProcessor.Apply(reader, intent);
    }
}
