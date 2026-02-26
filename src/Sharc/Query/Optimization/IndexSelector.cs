// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Core.Schema;
using Sharc.Query.Intent;

namespace Sharc.Query.Optimization;

/// <summary>
/// Selects the best index execution strategy (single index seek or rowid intersection)
/// for a set of sargable conditions.
/// </summary>
internal static class IndexSelector
{
    private readonly struct CandidatePlan
    {
        public required IndexSeekPlan Plan { get; init; }
        public required int Score { get; init; }
    }

    /// <summary>
    /// Evaluates sargable conditions against available indexes and returns the best execution plan.
    /// </summary>
    public static IndexExecutionPlan SelectBestPlan(
        List<SargableCondition> conditions, IReadOnlyList<IndexInfo> indexes)
    {
        if (conditions.Count == 0 || indexes.Count == 0)
            return default;

        IndexSeekPlan bestSingle = default;
        int bestSingleScore = -1;
        var intersectionCandidates = new List<CandidatePlan>(Math.Min(indexes.Count, 8));

        foreach (var index in indexes)
        {
            if (index.Columns.Count == 0)
                continue;

            if (TryBuildPlan(index, conditions, out var plan, out int score) && score > bestSingleScore)
            {
                bestSingle = plan;
                bestSingleScore = score;
            }

            // Intersection uses one first-column condition per index.
            if (TryBuildSingleColumnPlan(index, conditions, out var singleColPlan, out int singleColScore))
            {
                intersectionCandidates.Add(new CandidatePlan
                {
                    Plan = singleColPlan,
                    Score = singleColScore
                });
            }
        }

        if (bestSingle.Index == null)
            return default;

        var best = new IndexExecutionPlan
        {
            Strategy = IndexPlanStrategy.SingleIndex,
            Primary = bestSingle,
            Secondary = default,
            Score = bestSingleScore
        };

        if (CanConsiderIntersection(bestSingle, bestSingleScore) &&
            TryFindBestIntersection(intersectionCandidates, out var left, out var right, out int pairScore) &&
            pairScore > bestSingleScore + 40)
        {
            best = new IndexExecutionPlan
            {
                Strategy = IndexPlanStrategy.RowIdIntersection,
                Primary = left,
                Secondary = right,
                Score = pairScore
            };
        }

        return best;
    }

    /// <summary>
    /// Compatibility wrapper that returns only the primary single-index plan.
    /// </summary>
    public static IndexSeekPlan SelectBestIndex(
        List<SargableCondition> conditions, IReadOnlyList<IndexInfo> indexes)
    {
        return SelectBestPlan(conditions, indexes).Primary;
    }

    private static bool TryBuildPlan(
        IndexInfo index,
        List<SargableCondition> conditions,
        out IndexSeekPlan plan,
        out int score)
    {
        plan = default;
        score = -1;

        if (!TryFindBestConditionForColumn(index.Columns[0].Name, conditions, index, out var first))
            return false;

        int prefixCount = CountEqPrefixMatches(index, conditions, first.Op == IntentOp.Eq);
        var residual = new List<IndexColumnConstraint>(Math.Max(0, index.Columns.Count - 1));
        int matchedColumns = 1;
        int residualScore = 0;

        for (int i = 1; i < index.Columns.Count; i++)
        {
            if (!TryFindBestConditionForColumn(index.Columns[i].Name, conditions, index, out var condition))
                continue;

            residual.Add(BuildConstraint(condition, i));
            matchedColumns++;
            residualScore += ScoreOp(condition.Op) * 15;
        }

        int firstScore = ScoreCondition(first, index) * 100;
        int prefixBonus = Math.Max(0, prefixCount - 1) * 35;
        int matchedBonus = Math.Max(0, matchedColumns - 1) * 8;

        score = firstScore + prefixBonus + residualScore + matchedBonus;
        plan = BuildPlan(first, index, prefixCount, matchedColumns,
            residual.Count == 0 ? Array.Empty<IndexColumnConstraint>() : [.. residual]);
        return true;
    }

    private static bool TryBuildSingleColumnPlan(
        IndexInfo index,
        List<SargableCondition> conditions,
        out IndexSeekPlan plan,
        out int score)
    {
        plan = default;
        score = -1;

        if (!TryFindBestConditionForColumn(index.Columns[0].Name, conditions, index, out var first))
            return false;

        score = ScoreCondition(first, index) * 100;
        plan = BuildPlan(first, index, prefixCount: 1, matchedColumns: 1, Array.Empty<IndexColumnConstraint>());
        return true;
    }

    private static bool CanConsiderIntersection(in IndexSeekPlan bestSingle, int bestSingleScore)
    {
        if (bestSingle.Index == null)
            return false;

        if (bestSingleScore < 0)
            return false;

        // Exact unique lookups are already optimal.
        if (bestSingle.Index.IsUnique && bestSingle.SeekOp == IntentOp.Eq)
            return false;

        // Composite coverage is preferred over ad-hoc set intersection.
        if (bestSingle.MatchedColumnCount > 1)
            return false;

        return true;
    }

    private static bool TryFindBestIntersection(
        List<CandidatePlan> candidates,
        out IndexSeekPlan left,
        out IndexSeekPlan right,
        out int score)
    {
        left = default;
        right = default;
        score = -1;

        if (candidates.Count < 2)
            return false;

        for (int i = 0; i < candidates.Count; i++)
        {
            for (int j = i + 1; j < candidates.Count; j++)
            {
                var c1 = candidates[i];
                var c2 = candidates[j];

                if (c1.Plan.Index == null || c2.Plan.Index == null)
                    continue;

                if (c1.Plan.Index.Name.Equals(c2.Plan.Index.Name, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (c1.Plan.ConsumedColumn.Equals(c2.Plan.ConsumedColumn, StringComparison.OrdinalIgnoreCase))
                    continue;

                int pairScore = c1.Score + c2.Score + 30;
                if (pairScore <= score)
                    continue;

                // Build left from the more selective side to reduce hash-set size.
                if (c1.Score >= c2.Score)
                {
                    left = c1.Plan;
                    right = c2.Plan;
                }
                else
                {
                    left = c2.Plan;
                    right = c1.Plan;
                }

                score = pairScore;
            }
        }

        return score >= 0;
    }

    private static int CountEqPrefixMatches(
        IndexInfo index, List<SargableCondition> conditions, bool firstColumnIsEq)
    {
        int prefix = 1;
        if (!firstColumnIsEq)
            return prefix;

        for (int i = 1; i < index.Columns.Count; i++)
        {
            if (!TryFindEqConditionForColumn(index.Columns[i].Name, conditions, out _))
                break;
            prefix++;
        }

        return prefix;
    }

    private static bool TryFindEqConditionForColumn(
        string columnName,
        List<SargableCondition> conditions,
        out SargableCondition best)
    {
        best = default;
        bool found = false;

        foreach (var condition in conditions)
        {
            if (!condition.ColumnName.Equals(columnName, StringComparison.OrdinalIgnoreCase))
                continue;

            if (condition.Op != IntentOp.Eq || !IsSupportedCondition(condition))
                continue;

            best = condition;
            found = true;
            break;
        }

        return found;
    }

    private static bool TryFindBestConditionForColumn(
        string columnName,
        List<SargableCondition> conditions,
        IndexInfo index,
        out SargableCondition best)
    {
        best = default;
        bool found = false;
        int bestScore = -1;

        foreach (var condition in conditions)
        {
            if (!condition.ColumnName.Equals(columnName, StringComparison.OrdinalIgnoreCase))
                continue;

            if (!IsSupportedCondition(condition))
                continue;

            int score = ScoreCondition(condition, index);
            if (!found || score > bestScore)
            {
                best = condition;
                bestScore = score;
                found = true;
            }
        }

        return found;
    }

    private static bool IsSupportedCondition(SargableCondition condition)
    {
        if (condition.IsTextKey)
            return condition.Op == IntentOp.Eq && !string.IsNullOrEmpty(condition.TextValue);

        if (condition.IsIntegerKey || condition.IsRealKey)
        {
            return condition.Op is IntentOp.Eq or IntentOp.Gt or IntentOp.Gte
                or IntentOp.Lt or IntentOp.Lte or IntentOp.Between;
        }

        return false;
    }

    private static int ScoreCondition(SargableCondition condition, IndexInfo index)
    {
        return condition.Op switch
        {
            IntentOp.Eq when index.IsUnique => 4,
            IntentOp.Eq => 3,
            IntentOp.Between => 2,
            _ => 1 // Gt, Gte, Lt, Lte
        };
    }

    private static int ScoreOp(IntentOp op)
    {
        return op switch
        {
            IntentOp.Eq => 3,
            IntentOp.Between => 2,
            _ => 1
        };
    }

    private static IndexColumnConstraint BuildConstraint(SargableCondition condition, int indexColumnOrdinal)
    {
        bool hasUpperBound = condition.Op == IntentOp.Between;
        return new IndexColumnConstraint
        {
            ColumnOrdinal = indexColumnOrdinal,
            Op = condition.Op,
            IntegerValue = condition.IntegerValue,
            IntegerHighValue = hasUpperBound ? condition.HighValue : 0,
            RealValue = condition.RealValue,
            RealHighValue = hasUpperBound ? condition.RealHighValue : 0,
            IsIntegerKey = condition.IsIntegerKey,
            IsRealKey = condition.IsRealKey,
            IsTextKey = condition.IsTextKey,
            TextValue = condition.TextValue
        };
    }

    private static IndexSeekPlan BuildPlan(
        SargableCondition firstCondition,
        IndexInfo index,
        int prefixCount,
        int matchedColumns,
        IndexColumnConstraint[] residualConstraints)
    {
        bool hasUpperBound = firstCondition.Op == IntentOp.Between;

        return new IndexSeekPlan
        {
            Index = index,
            SeekKey = firstCondition.IntegerValue,
            SeekRealKey = firstCondition.RealValue,
            SeekOp = firstCondition.Op,
            UpperBound = hasUpperBound ? firstCondition.HighValue : 0,
            UpperBoundReal = hasUpperBound ? firstCondition.RealHighValue : 0,
            HasUpperBound = hasUpperBound,
            ConsumedColumn = firstCondition.ColumnName,
            PrefixMatchCount = prefixCount,
            MatchedColumnCount = matchedColumns,
            IsRealKey = firstCondition.IsRealKey,
            IsTextKey = firstCondition.IsTextKey,
            TextValue = firstCondition.TextValue,
            ResidualConstraints = residualConstraints
        };
    }
}

internal enum IndexPlanStrategy : byte
{
    None = 0,
    SingleIndex = 1,
    RowIdIntersection = 2
}

/// <summary>
/// Describes the selected index execution strategy.
/// </summary>
internal struct IndexExecutionPlan
{
    public IndexPlanStrategy Strategy;
    public IndexSeekPlan Primary;
    public IndexSeekPlan Secondary;
    public int Score;
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

    /// <summary>The real key value for SeekFirst(double).</summary>
    public double SeekRealKey;

    /// <summary>The comparison operator driving the seek.</summary>
    public IntentOp SeekOp;

    /// <summary>Upper bound for Between/Lt/Lte termination.</summary>
    public long UpperBound;

    /// <summary>Upper bound for Between/Lt/Lte termination on real keys.</summary>
    public double UpperBoundReal;

    /// <summary>True if the plan has an upper bound (Between).</summary>
    public bool HasUpperBound;

    /// <summary>Which column the index covers (first column for seek).</summary>
    public string ConsumedColumn;

    /// <summary>Number of leading index columns matched by Eq conditions (&gt;=1).</summary>
    public int PrefixMatchCount;

    /// <summary>Total number of index key columns covered by planner constraints.</summary>
    public int MatchedColumnCount;

    /// <summary>True if seeking on a real key.</summary>
    public bool IsRealKey;

    /// <summary>True if seeking on a text key instead of integer.</summary>
    public bool IsTextKey;

    /// <summary>Text value for text key seeks.</summary>
    public string? TextValue;

    /// <summary>
    /// Additional constraints on subsequent index key columns, evaluated before table row materialization.
    /// </summary>
    public IndexColumnConstraint[]? ResidualConstraints;
}

/// <summary>
/// A single planner constraint bound to an index key column ordinal.
/// </summary>
internal struct IndexColumnConstraint
{
    public int ColumnOrdinal;
    public IntentOp Op;
    public long IntegerValue;
    public long IntegerHighValue;
    public double RealValue;
    public double RealHighValue;
    public bool IsIntegerKey;
    public bool IsRealKey;
    public bool IsTextKey;
    public string? TextValue;
}
