// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Core.Schema;
using Sharc.Query.Intent;

namespace Sharc.Query.Optimization;

/// <summary>
/// Selects the best index to use for a set of sargable conditions.
/// </summary>
internal static class IndexSelector
{
    /// <summary>
    /// Evaluates sargable conditions against available indexes and returns the best seek plan.
    /// Scoring: Eq on unique index (4) > Eq on non-unique (3) > Between (2) > Gt/Gte/Lt/Lte (1).
    /// Only the first column of each index is considered for matching.
    /// </summary>
    public static IndexSeekPlan SelectBestIndex(
        List<SargableCondition> conditions, IReadOnlyList<IndexInfo> indexes)
    {
        if (conditions.Count == 0 || indexes.Count == 0)
            return default;

        IndexSeekPlan best = default;
        int bestScore = -1;

        foreach (var condition in conditions)
        {
            foreach (var index in indexes)
            {
                if (index.Columns.Count == 0)
                    continue;

                // Only match on the first column of the index
                if (!index.Columns[0].Name.Equals(condition.ColumnName, StringComparison.OrdinalIgnoreCase))
                    continue;

                int score = ScoreCondition(condition, index);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = BuildPlan(condition, index);
                }
            }
        }

        return best;
    }

    private static int ScoreCondition(SargableCondition condition, IndexInfo index)
    {
        return condition.Op switch
        {
            IntentOp.Eq when index.IsUnique => 4,
            IntentOp.Eq => 3,
            IntentOp.Between => 2,
            _ => 1  // Gt, Gte, Lt, Lte
        };
    }

    private static IndexSeekPlan BuildPlan(SargableCondition condition, IndexInfo index)
    {
        return new IndexSeekPlan
        {
            Index = index,
            SeekKey = condition.IntegerValue,
            SeekOp = condition.Op,
            UpperBound = condition.Op == IntentOp.Between ? condition.HighValue : 0,
            HasUpperBound = condition.Op == IntentOp.Between,
            ConsumedColumn = condition.ColumnName,
            IsTextKey = condition.IsTextKey,
            TextValue = condition.TextValue
        };
    }
}

/// <summary>
/// Describes an index seek plan chosen by <see cref="IndexSelector"/>.
/// </summary>
internal struct IndexSeekPlan
{
    /// <summary>The chosen index, or null if no usable index was found.</summary>
    public IndexInfo? Index;

    /// <summary>The integer key value for SeekFirst.</summary>
    public long SeekKey;

    /// <summary>The comparison operator driving the seek.</summary>
    public IntentOp SeekOp;

    /// <summary>Upper bound for Between/Lt/Lte termination.</summary>
    public long UpperBound;

    /// <summary>True if the plan has an upper bound (Between).</summary>
    public bool HasUpperBound;

    /// <summary>Which column the index covers.</summary>
    public string ConsumedColumn;

    /// <summary>True if seeking on a text key instead of integer.</summary>
    public bool IsTextKey;

    /// <summary>Text value for text key seeks.</summary>
    public string? TextValue;
}
