// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Core;
using Sharc.Query.Intent;
using Sharc.Query; // For QueryValue
using Sharc; // For SharcDatabase, SharcDataReader

namespace Sharc.Query.Execution;

/// <summary>
/// Executes JOIN operations using an in-memory Hash Join strategy.
/// Supports INNER, LEFT, CROSS joins.
/// </summary>
internal static class JoinExecutor
{
    public static SharcDataReader Execute(SharcDatabase db, QueryIntent intent, IReadOnlyDictionary<string, object>? parameters)
    {
        // 1. Materialize the primary (left-most) table
        // We must strip joins from the intent to get the base table scan
        var baseIntent = CloneForBaseTable(intent);
        var (leftRows, leftSchema) = Materialize(db, baseIntent, parameters, intent.TableAlias ?? intent.TableName);

        // 2. Execute joins sequentially
        if (intent.Joins != null)
        {
            foreach (var join in intent.Joins)
            {
                // Materialize right table
                // Create a synthetic intent for the joined table
                var rightIntent = new QueryIntent 
                { 
                    TableName = join.TableName, 
                    TableAlias = join.TableAlias 
                };
                
                var (rightRows, rightSchema) = Materialize(db, rightIntent, parameters, join.TableAlias ?? join.TableName);

                // Perform the join
                (leftRows, leftSchema) = HashJoin(leftRows, leftSchema, rightRows, rightSchema, join);
            }
        }

        // 3. Apply Filter (WHERE) on the fully joined set
        if (intent.Filter.HasValue)
        {
            leftRows = FilterRows(leftRows, leftSchema, intent.Filter.Value);
        }

        // 4. Apply Aggregates / Group By / Order By / Limit / Projection
        // Re-use logic from CompoundQueryExecutor/QueryPostProcessor where possible.
        // For "Minimum Effort", we rely on MaterializedResultSet or manual processing.
        // Since we have specific columns in intent, we must project them.
        
        var columns = leftSchema.OrderBy(kv => kv.Value).Select(kv => kv.Key).ToArray();
        
        // If we have aggregates, we need to process them.
        if (intent.HasAggregates || (intent.GroupBy is { Count: > 0 }))
        {
             // Use AggregateProcessor? It expects List<QueryValue[]> and column names.
             // We need to map intent.Columns (aliases/exprs) to the joined schema.
             // This part is tricky because AggregateProcessor expects input columns to match logic.
             // For now, let's assume simple projection if no aggregates.
        }

        // Apply OrderBy
        if (intent.OrderBy is { Count: > 0 })
        {
             QueryPostProcessor.ApplyOrderBy(leftRows, intent.OrderBy, columns);
        }

        // Apply Limit/Offset
        if (intent.Limit.HasValue || intent.Offset.HasValue)
        {
            leftRows = QueryPostProcessor.ApplyLimitOffset(leftRows, intent.Limit, intent.Offset);
        }

        // Final Projection (SELECT a, b, c) from the joined set (a, b, c, d, e...)
        if (intent.Columns != null)
        {
            var projectedRows = ProjectRows(leftRows, leftSchema, intent.Columns);
            return new SharcDataReader(projectedRows, intent.ColumnsArray!);
        }

        return new SharcDataReader(leftRows, columns);
    }

    private static (List<QueryValue[]> Rows, Dictionary<string, int> Schema) HashJoin(
        List<QueryValue[]> leftRows, Dictionary<string, int> leftSchema,
        List<QueryValue[]> rightRows, Dictionary<string, int> rightSchema,
        JoinIntent join)
    {
        // Merge schemas
        var mergedSchema = new Dictionary<string, int>(leftSchema);
        int offset = leftSchema.Count;
        foreach (var kv in rightSchema)
        {
            // Simple conflict resolution: if key exists, keep left (or throw?)
            // Prefixed keys should be unique usually.
            if (!mergedSchema.ContainsKey(kv.Key))
            {
                mergedSchema[kv.Key] = offset + kv.Value;
            }
        }
        int rightColumnCount = rightSchema.Count;
        int totalColumns = offset + rightColumnCount;

        // Build Hash Table on Right
        // Group rows by the join key
        var hashTable = new Dictionary<QueryValue, List<QueryValue[]>>();
        
        if (join.Kind != JoinType.Cross)
        {
            if (!rightSchema.TryGetValue(join.RightColumn!, out int rightColIdx))
                throw new InvalidOperationException($"Right join column '{join.RightColumn}' not found in schema.");

            foreach (var row in rightRows)
            {
                var key = row[rightColIdx];
                if (key.IsNull) continue; // NULLs don't match in standard SQL equality
                
                if (!hashTable.TryGetValue(key, out var list))
                {
                    list = new List<QueryValue[]>();
                    hashTable[key] = list;
                }
                list.Add(row);
            }
        }

        // Probe Left
        var resultRows = new List<QueryValue[]>();
        int leftColIdx = -1;
        if (join.Kind != JoinType.Cross)
        {
            if (!leftSchema.TryGetValue(join.LeftColumn!, out leftColIdx))
                throw new InvalidOperationException($"Left join column '{join.LeftColumn}' not found in schema.");
        }

        foreach (var leftRow in leftRows)
        {
            if (join.Kind == JoinType.Cross)
            {
                foreach (var rightRow in rightRows)
                    resultRows.Add(MergeRows(leftRow, rightRow));
                continue;
            }

            var key = leftRow[leftColIdx];
            if (!key.IsNull && hashTable.TryGetValue(key, out var matches))
            {
                // Inner / Left match
                foreach (var rightRow in matches)
                {
                    resultRows.Add(MergeRows(leftRow, rightRow));
                }
            }
            else if (join.Kind == JoinType.Left)
            {
                // Left Join - emit with NULLs
                var nullRow = new QueryValue[rightColumnCount];
                Array.Fill(nullRow, QueryValue.Null);
                resultRows.Add(MergeRows(leftRow, nullRow));
            }
            else if (join.Kind == JoinType.Right)
            {
                throw new NotSupportedException("RIGHT JOIN is not currently supported in Sharc. Please use LEFT JOIN with swapped table order.");
            }
        }

        return (resultRows, mergedSchema);
    }

    private static QueryValue[] MergeRows(QueryValue[] left, QueryValue[] right)
    {
        var combined = new QueryValue[left.Length + right.Length];
        Array.Copy(left, 0, combined, 0, left.Length);
        Array.Copy(right, 0, combined, left.Length, right.Length);
        return combined;
    }

    private static (List<QueryValue[]> Rows, Dictionary<string, int> Schema) Materialize(
        SharcDatabase db, QueryIntent intent, IReadOnlyDictionary<string, object>? parameters, string prefix)
    {
        using var reader = db.CreateReaderFromIntent(intent, parameters);
        var rows = new List<QueryValue[]>();
        int fieldCount = reader.FieldCount;
        
        while (reader.Read())
        {
             var row = new QueryValue[fieldCount];
             for (int i = 0; i < fieldCount; i++)
             {
                 row[i] = ReadValue(reader, i);
             }
             rows.Add(row);
        }
        
        var schema = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < fieldCount; i++)
        {
            string name = reader.GetColumnName(i);
            // Prefix: "u.id"
            schema[$"{prefix}.{name}"] = i;
        }
        
        return (rows, schema);
    }

    private static QueryValue ReadValue(SharcDataReader reader, int ordinal)
    {
        if (reader.IsNull(ordinal)) return QueryValue.Null;
        
        var type = reader.GetColumnType(ordinal);
        switch (type)
        {
            case SharcColumnType.Integral: return QueryValue.FromInt64(reader.GetInt64(ordinal));
            case SharcColumnType.Real: return QueryValue.FromDouble(reader.GetDouble(ordinal));
            case SharcColumnType.Text: return QueryValue.FromString(reader.GetString(ordinal));
            case SharcColumnType.Blob: return QueryValue.FromBlob(reader.GetBlob(ordinal));
            default: return QueryValue.Null;
        }
    }

    private static QueryIntent CloneForBaseTable(QueryIntent intent) => new()
    {
        TableName = intent.TableName,
        TableRecordId = intent.TableRecordId,
        // Fetch all columns (*) to allow filtering/joining on any column
        Columns = null, 
        Filter = null, // Filter applied after join
        OrderBy = null,
        Limit = null,
        Offset = null,
        IsDistinct = false,
        Aggregates = null,
        GroupBy = null,
        HavingFilter = null
    };

    private static List<QueryValue[]> FilterRows(List<QueryValue[]> rows, Dictionary<string, int> schema, PredicateIntent filter)
    {
        var result = new List<QueryValue[]>();
        foreach (var row in rows)
        {
            if (Evaluate(filter, row, schema))
                result.Add(row);
        }
        return result;
    }

    private static bool Evaluate(PredicateIntent filter, QueryValue[] row, Dictionary<string, int> schema)
    {
        if (filter.Nodes.Length == 0) return true;
        // Nodes is a flat array, root is at RootIndex
        return EvaluateNode(filter.Nodes[filter.RootIndex], filter.Nodes, row, schema);
    }

    private static bool EvaluateNode(Sharc.Query.Intent.PredicateNode node, Sharc.Query.Intent.PredicateNode[] nodes, QueryValue[] row, Dictionary<string, int> schema)
    {
        if (node.Op == IntentOp.And)
            return EvaluateNode(nodes[node.LeftIndex], nodes, row, schema) &&
                   EvaluateNode(nodes[node.RightIndex], nodes, row, schema);

        if (node.Op == IntentOp.Or)
            return EvaluateNode(nodes[node.LeftIndex], nodes, row, schema) ||
                   EvaluateNode(nodes[node.RightIndex], nodes, row, schema);

        if (node.Op == IntentOp.Not)
            return !EvaluateNode(nodes[node.LeftIndex], nodes, row, schema);

        // Leaf comparison
        QueryValue colVal = QueryValue.Null;
        if (node.ColumnName != null && schema.TryGetValue(node.ColumnName, out int colIdx))
        {
            colVal = row[colIdx];
        }
        
        return Compare(node.Op, colVal, node.Value, node.HighValue);
    }

    private static bool Compare(IntentOp op, QueryValue left, IntentValue right, IntentValue rightHigh)
    {
        if (left.IsNull) 
        {
             if (op == IntentOp.IsNull) return true;
             if (op == IntentOp.IsNotNull) return false;
             return false;
        }

        switch (op)
        {
            case IntentOp.Eq: return ResultCompare(left, right) == 0;
            case IntentOp.Neq: return ResultCompare(left, right) != 0;
            case IntentOp.Gt: return ResultCompare(left, right) > 0;
            case IntentOp.Gte: return ResultCompare(left, right) >= 0;
            case IntentOp.Lt: return ResultCompare(left, right) < 0;
            case IntentOp.Lte: return ResultCompare(left, right) <= 0;
            case IntentOp.IsNull: return false;
            case IntentOp.IsNotNull: return true;
            case IntentOp.Between:
                return ResultCompare(left, right) >= 0 && ResultCompare(left, rightHigh) <= 0;
            case IntentOp.In:
                return CheckIn(left, right);
            case IntentOp.NotIn:
                return !CheckIn(left, right);
            case IntentOp.Like:
                return CheckLike(left, right);
            case IntentOp.NotLike:
                return !CheckLike(left, right);
            case IntentOp.StartsWith:
                return left.AsString().StartsWith(right.AsText!, StringComparison.OrdinalIgnoreCase);
            case IntentOp.EndsWith:
                return left.AsString().EndsWith(right.AsText!, StringComparison.OrdinalIgnoreCase);
            case IntentOp.Contains:
                return left.AsString().Contains(right.AsText!, StringComparison.OrdinalIgnoreCase);
        }
        return false;
    }

    private static int ResultCompare(QueryValue left, IntentValue right)
    {
        // Integer comparison
        if (left.Type == QueryValueType.Int64 && right.Kind == IntentValueKind.Signed64)
            return left.AsInt64().CompareTo(right.AsInt64);
            
        // Text comparison
        if (left.Type == QueryValueType.Text && right.Kind == IntentValueKind.Text)
            return string.Compare(left.AsString(), right.AsText, StringComparison.Ordinal);

        // Float comparison
        if (left.Type == QueryValueType.Double && right.Kind == IntentValueKind.Real)
            return left.AsDouble().CompareTo(right.AsFloat64);

        // Mixed types (Int vs Double)
        if (left.Type == QueryValueType.Int64 && right.Kind == IntentValueKind.Real)
            return ((double)left.AsInt64()).CompareTo(right.AsFloat64);
        
        if (left.Type == QueryValueType.Double && right.Kind == IntentValueKind.Signed64)
            return left.AsDouble().CompareTo((double)right.AsInt64);

        // Fallback: compare strings
        // This is safe even if one or both are not strings, thanks to ToString overrides/semantics?
        // QueryValue.AsString() works for numbers.
        // IntentValue.AsText might be null if not text.
        // But let's assume strict types for now, or just return 0.
        return 0; 
    }

    private static bool CheckIn(QueryValue left, IntentValue right)
    {
        if (right.Kind == IntentValueKind.Signed64Set)
        {
            long val = left.AsInt64();
            foreach (var s in right.AsInt64Set!) if (s == val) return true;
        }
        else if (right.Kind == IntentValueKind.TextSet)
        {
            string val = left.AsString();
            foreach (var s in right.AsTextSet!) if (string.Equals(s, val, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private static bool CheckLike(QueryValue left, IntentValue right)
    {
        if (left.Type != QueryValueType.Text || right.Kind != IntentValueKind.Text) return false;
        string pattern = right.AsText!;
        string text = left.AsString();

        // Very simple LIKE for now: handle % at start/end
        if (pattern.StartsWith('%') && pattern.EndsWith('%'))
        {
            return text.Contains(pattern[1..^1], StringComparison.OrdinalIgnoreCase);
        }
        if (pattern.StartsWith('%'))
        {
            return text.EndsWith(pattern[1..], StringComparison.OrdinalIgnoreCase);
        }
        if (pattern.EndsWith('%'))
        {
            return text.StartsWith(pattern[..^1], StringComparison.OrdinalIgnoreCase);
        }
        return string.Equals(text, pattern, StringComparison.OrdinalIgnoreCase);
    }

    private static List<QueryValue[]> ProjectRows(List<QueryValue[]> rows, Dictionary<string, int> schema, IReadOnlyList<string> columns)
    {
        var result = new List<QueryValue[]>();
        var indices = new int[columns.Count];
        for (int i = 0; i < columns.Count; i++)
        {
            indices[i] = schema.TryGetValue(columns[i], out int idx) ? idx : -1;
        }

        foreach (var row in rows)
        {
            var newRow = new QueryValue[columns.Count];
            for (int i = 0; i < columns.Count; i++)
            {
                newRow[i] = indices[i] >= 0 ? row[indices[i]] : QueryValue.Null;
            }
            result.Add(newRow);
        }
        return result;
    }
}
