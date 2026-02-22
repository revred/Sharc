// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using IntentPredicateNode = Sharc.Query.Intent.PredicateNode;
using Sharc.Query.Intent;

namespace Sharc.Query.Optimization;

/// <summary>
/// Extracts sargable (Search ARGument ABLE) conditions from a <see cref="PredicateIntent"/>
/// that can be accelerated by index seeks.
/// </summary>
internal static class PredicateAnalyzer
{
    /// <summary>
    /// Walks the predicate tree and extracts leaf conditions suitable for index lookup.
    /// Only conditions under AND branches are extracted; OR and NOT branches are skipped
    /// because they cannot be satisfied by a single index seek.
    /// </summary>
    /// <param name="filter">The predicate intent to analyze.</param>
    /// <param name="tableAlias">Optional table alias to strip from column names (e.g., "u" strips "u.id" to "id").</param>
    /// <returns>List of sargable conditions that may be index-accelerated.</returns>
    public static List<SargableCondition> ExtractSargableConditions(PredicateIntent filter, string? tableAlias)
    {
        var result = new List<SargableCondition>();
        if (filter.Nodes.Length == 0 || filter.RootIndex < 0)
            return result;

        ExtractFromNode(filter.Nodes, filter.RootIndex, tableAlias, result);
        return result;
    }

    private static void ExtractFromNode(
        IntentPredicateNode[] nodes, int index, string? tableAlias, List<SargableCondition> result)
    {
        var node = nodes[index];

        switch (node.Op)
        {
            case IntentOp.And:
                // AND: both sides can independently contribute sargable conditions
                ExtractFromNode(nodes, node.LeftIndex, tableAlias, result);
                ExtractFromNode(nodes, node.RightIndex, tableAlias, result);
                return;

            case IntentOp.Or:
            case IntentOp.Not:
                // OR/NOT: cannot be satisfied by a single index seek
                return;

            case IntentOp.Eq:
            case IntentOp.Gt:
            case IntentOp.Gte:
            case IntentOp.Lt:
            case IntentOp.Lte:
            case IntentOp.Between:
                TryExtractLeaf(in node, tableAlias, result);
                return;

            default:
                // LIKE, IN, IsNull, etc. — not sargable for index seeks
                return;
        }
    }

    private static void TryExtractLeaf(
        in IntentPredicateNode node, string? tableAlias, List<SargableCondition> result)
    {
        if (node.ColumnName == null)
            return;

        string columnName = StripAlias(node.ColumnName, tableAlias);

        if (node.Value.Kind == IntentValueKind.Signed64)
        {
            long highValue = node.Op == IntentOp.Between && node.HighValue.Kind == IntentValueKind.Signed64
                ? node.HighValue.AsInt64
                : 0;

            result.Add(new SargableCondition
            {
                ColumnName = columnName,
                Op = node.Op,
                IntegerValue = node.Value.AsInt64,
                HighValue = highValue,
                IsIntegerKey = true,
                IsTextKey = false,
                TextValue = null
            });
        }
        else if (node.Value.Kind == IntentValueKind.Text && node.Op == IntentOp.Eq)
        {
            // Text equality — can be index-accelerated with text SeekFirst
            result.Add(new SargableCondition
            {
                ColumnName = columnName,
                Op = IntentOp.Eq,
                IntegerValue = 0,
                HighValue = 0,
                IsIntegerKey = false,
                IsTextKey = true,
                TextValue = node.Value.AsText
            });
        }
    }

    private static string StripAlias(string columnName, string? tableAlias)
    {
        if (tableAlias == null)
            return columnName;

        // "u.user_id" with alias "u" -> "user_id"
        if (columnName.Length > tableAlias.Length + 1 &&
            columnName.StartsWith(tableAlias, StringComparison.OrdinalIgnoreCase) &&
            columnName[tableAlias.Length] == '.')
        {
            return columnName[(tableAlias.Length + 1)..];
        }

        return columnName;
    }
}

/// <summary>
/// A single condition extracted from a predicate tree that can be accelerated by an index seek.
/// </summary>
internal struct SargableCondition
{
    /// <summary>Column name with alias stripped.</summary>
    public string ColumnName;

    /// <summary>Comparison operator.</summary>
    public IntentOp Op;

    /// <summary>Integer comparison value (for Eq/Gt/Gte/Lt/Lte/Between lower bound).</summary>
    public long IntegerValue;

    /// <summary>Upper bound for Between.</summary>
    public long HighValue;

    /// <summary>True if this condition compares an integer value.</summary>
    public bool IsIntegerKey;

    /// <summary>True if this condition compares a text value.</summary>
    public bool IsTextKey;

    /// <summary>Text comparison value (for text equality seeks).</summary>
    public string? TextValue;
}
