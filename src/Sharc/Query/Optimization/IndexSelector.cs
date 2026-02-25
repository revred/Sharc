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
    /// Scoring: base score (Eq unique=4, Eq=3, Between=2, range=1) × composite prefix match count.
    /// Composite indexes are scored by how many leading columns have Eq conditions,
    /// enabling (a, b) to be preferred over (a) when both a=? AND b=? are present.
    /// </summary>
    public static IndexSeekPlan SelectBestIndex(
        List<SargableCondition> conditions, IReadOnlyList<IndexInfo> indexes)
    {
        if (conditions.Count == 0 || indexes.Count == 0)
            return default;

        IndexSeekPlan best = default;
        int bestScore = -1;

        foreach (var index in indexes)
        {
            if (index.Columns.Count == 0)
                continue;

            // Find the condition matching the first column (required for seek)
            SargableCondition? firstColumnCondition = null;
            foreach (var condition in conditions)
            {
                if (index.Columns[0].Name.Equals(condition.ColumnName, StringComparison.OrdinalIgnoreCase))
                {
                    // Prefer Eq conditions for the first column (enables composite prefix matching)
                    if (firstColumnCondition == null || ScoreCondition(condition, index) > ScoreCondition(firstColumnCondition.Value, index))
                        firstColumnCondition = condition;
                }
            }

            if (firstColumnCondition == null)
                continue;

            int baseScore = ScoreCondition(firstColumnCondition.Value, index);

            // Count additional Eq prefix matches on subsequent index columns
            int prefixCount = 1;
            if (firstColumnCondition.Value.Op == IntentOp.Eq)
            {
                for (int c = 1; c < index.Columns.Count; c++)
                {
                    bool matched = false;
                    foreach (var condition in conditions)
                    {
                        if (condition.Op == IntentOp.Eq &&
                            index.Columns[c].Name.Equals(condition.ColumnName, StringComparison.OrdinalIgnoreCase))
                        {
                            matched = true;
                            break;
                        }
                    }
                    if (!matched) break; // prefix chain broken
                    prefixCount++;
                }
            }

            int score = baseScore * prefixCount;
            if (score > bestScore)
            {
                bestScore = score;
                best = BuildPlan(firstColumnCondition.Value, index, prefixCount);
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

    private static IndexSeekPlan BuildPlan(SargableCondition condition, IndexInfo index, int prefixCount = 1)
    {
        return new IndexSeekPlan
        {
            Index = index,
            SeekKey = condition.IntegerValue,
            SeekOp = condition.Op,
            UpperBound = condition.Op == IntentOp.Between ? condition.HighValue : 0,
            HasUpperBound = condition.Op == IntentOp.Between,
            ConsumedColumn = condition.ColumnName,
            PrefixMatchCount = prefixCount,
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

    /// <summary>Which column the index covers (first column for seek).</summary>
    public string ConsumedColumn;

    /// <summary>Number of leading index columns matched by Eq conditions (≥1).</summary>
    public int PrefixMatchCount;

    /// <summary>True if seeking on a text key instead of integer.</summary>
    public bool IsTextKey;

    /// <summary>Text value for text key seeks.</summary>
    public string? TextValue;
}
