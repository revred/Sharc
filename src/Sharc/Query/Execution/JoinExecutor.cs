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
    private sealed class TableExecutionNode
    {
        public required string TableName { get; init; }
        public required string Alias { get; init; }
        public HashSet<string> RequiredColumns { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<Sharc.Query.Intent.PredicateNode> PushdownNodes { get; } = new();
        public int PushdownRootIndex { get; set; } = -1;

        // Cached to avoid repeated ToArray() allocations
        private PredicateIntent? _cachedPushdownFilter;
        private int _cachedPushdownCount = -1;

        public PredicateIntent? PushdownFilter
        {
            get
            {
                if (PushdownNodes.Count == 0) return null;
                if (_cachedPushdownCount == PushdownNodes.Count) return _cachedPushdownFilter;
                _cachedPushdownFilter = new PredicateIntent(PushdownNodes.ToArray(), PushdownRootIndex);
                _cachedPushdownCount = PushdownNodes.Count;
                return _cachedPushdownFilter;
            }
        }
    }

    private sealed class JoinExecutionPlan
    {
        public Dictionary<string, TableExecutionNode> Nodes { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<Sharc.Query.Intent.PredicateNode> ResidualNodes { get; } = new();
        public int ResidualRootIndex { get; set; } = -1;

        // Cached to avoid repeated ToArray() allocations
        private PredicateIntent? _cachedResidualFilter;
        private int _cachedResidualCount = -1;

        public PredicateIntent? ResidualFilter
        {
            get
            {
                if (ResidualNodes.Count == 0) return null;
                if (_cachedResidualCount == ResidualNodes.Count) return _cachedResidualFilter;
                _cachedResidualFilter = new PredicateIntent(ResidualNodes.ToArray(), ResidualRootIndex);
                _cachedResidualCount = ResidualNodes.Count;
                return _cachedResidualFilter;
            }
        }
    }

    /// <summary>
    /// Executes a JOIN query using a Hash Join strategy.
    /// Builds an execution plan that pushes filters down to individual tables,
    /// materializes the primary table, then probes each joined table using a
    /// hash map on the join key. Residual cross-table predicates are evaluated
    /// post-join. Returns a streaming reader over the matched rows.
    /// </summary>
    public static SharcDataReader Execute(
        SharcDatabase db, QueryIntent intent, IReadOnlyDictionary<string, object>? parameters,
        CoteMap? coteResults = null)
    {
        // 1. Plan the execution: identify required columns and pushable filters
        var plan = BuildPlan(intent);

        // 2. Start with the primary (left-most) table as the probe stream
        var primaryAlias = intent.TableAlias ?? intent.TableName;
        var primaryNode = plan.Nodes[primaryAlias];
        var baseIntent = CloneWithPushdown(intent, primaryNode);

        // We use MaterializeRows to get an IEnumerable for the primary table
        var (leftRows, leftSchema) = MaterializeRows(db, baseIntent, parameters, primaryAlias, coteResults);
        IEnumerable<QueryValue[]> probeStream = leftRows;

        // When the downstream path will project (creating new arrays from merged rows),
        // the merged arrays are consumed as scratch buffers and can be reused — one
        // allocation per join stage instead of one per result row.
        bool willProject = intent.Columns != null;
        bool willMaterialize = intent.OrderBy is { Count: > 0 };
        bool canReuseJoinBuffer = willProject && !willMaterialize;

        // 3. Chain joins sequentially
        if (intent.Joins != null)
        {
            foreach (var join in intent.Joins)
            {
                var joinAlias = join.TableAlias ?? join.TableName;
                var joinNode = plan.Nodes[joinAlias];
                var rightIntent = CloneWithPushdown(new QueryIntent { TableName = join.TableName, TableAlias = join.TableAlias }, joinNode);

                var (rightRows, rightSchema) = MaterializeRows(db, rightIntent, parameters, joinAlias, coteResults);

                // Merge schemas (always [left, right]) — pre-sized to avoid rehashing
                var mergedSchema = new Dictionary<string, int>(leftSchema.Count + rightSchema.Count, StringComparer.OrdinalIgnoreCase);
                foreach (var kv in leftSchema)
                    mergedSchema[kv.Key] = kv.Value;
                int offset = leftSchema.Count;
                foreach (var kv in rightSchema)
                {
                    if (!mergedSchema.ContainsKey(kv.Key))
                    {
                        mergedSchema[kv.Key] = offset + kv.Value;
                    }
                }

                //perform the streaming join
                probeStream = HashJoin(probeStream, leftSchema, rightRows, rightSchema, join, canReuseJoinBuffer);
                leftSchema = mergedSchema;
            }
        }

        // 4. Apply residual Filter (WHERE) on the joined stream
        var residualFilter = plan.ResidualFilter;
        if (residualFilter.HasValue)
        {
            probeStream = FilterRows(probeStream, leftSchema, residualFilter.Value);
        }

        // 5. Materialize if OrderBy is needed, otherwise keep streaming
        var columns = BuildColumnNames(leftSchema);
        
        if (intent.OrderBy is { Count: > 0 })
        {
             var finalRows = probeStream.ToList();
             QueryPostProcessor.ApplyOrderBy(finalRows, intent.OrderBy, columns);
             
             if (intent.Limit.HasValue || intent.Offset.HasValue)
             {
                 finalRows = QueryPostProcessor.ApplyLimitOffset(finalRows, intent.Limit, intent.Offset);
             }

             if (intent.Columns != null)
             {
                 var projectedRows = ProjectRows(finalRows, leftSchema, intent.Columns);
                 return new SharcDataReader(projectedRows, intent.ColumnsArray!);
             }
             return new SharcDataReader(finalRows, columns);
        }

        // 6. Streaming Limit/Offset if no OrderBy — avoid materializing the entire result
        if (intent.Limit.HasValue || intent.Offset.HasValue)
        {
            probeStream = StreamingLimitOffset(probeStream, (int)(intent.Offset ?? 0), (int?)intent.Limit);
            if (intent.Columns != null)
            {
                 var projectedStream = ProjectRows(probeStream, leftSchema, intent.Columns);
                 return new SharcDataReader(projectedStream, intent.ColumnsArray!);
            }
            return new SharcDataReader(probeStream, columns);
        }

        // 7. Full Streaming Projection
        if (intent.Columns != null)
        {
            var projectedStream = ProjectRows(probeStream, leftSchema, intent.Columns);
            return new SharcDataReader(projectedStream, intent.ColumnsArray!);
        }

        return new SharcDataReader(probeStream, columns);
    }

    private static IEnumerable<QueryValue[]> HashJoin(
        IEnumerable<QueryValue[]> leftRows, Dictionary<string, int> leftSchema,
        IEnumerable<QueryValue[]> rightRows, Dictionary<string, int> rightSchema,
        JoinIntent join, bool reuseBuffer = false)
    {
        int rightColumnCount = rightSchema.Count;
        int leftColumnCount = leftSchema.Count;
        int mergedWidth = leftColumnCount + rightColumnCount;

        // For INNER JOIN, build the hash table on the smaller side to reduce
        // hash bucket memory. LEFT/CROSS must always build on right.
        bool swapped = false;
        RowSet buildRows;
        IEnumerable<QueryValue[]> probeRows;
        Dictionary<string, int> buildSchema, probeSchema;
        string? buildCol, probeCol;

        var rightList = (rightRows as RowSet) ?? rightRows.ToList();

        if (join.Kind == JoinType.Inner)
        {
            var leftList = (leftRows as RowSet) ?? leftRows.ToList();
            if (leftList.Count <= rightList.Count)
            {
                // Build on left (smaller), probe with right
                buildRows = leftList;
                probeRows = rightList;
                buildSchema = leftSchema;
                probeSchema = rightSchema;
                buildCol = join.LeftColumn;
                probeCol = join.RightColumn;
                swapped = true;
            }
            else
            {
                buildRows = rightList;
                probeRows = leftList;
                buildSchema = rightSchema;
                probeSchema = leftSchema;
                buildCol = join.RightColumn;
                probeCol = join.LeftColumn;
            }
        }
        else
        {
            // LEFT/CROSS: always build on right, stream left as probe
            buildRows = rightList;
            probeRows = leftRows;
            buildSchema = rightSchema;
            probeSchema = leftSchema;
            buildCol = join.RightColumn;
            probeCol = join.LeftColumn;
        }

        // Build Hash Table
        var hashTable = new Dictionary<QueryValue, RowSet>(buildRows.Count);
        if (join.Kind != JoinType.Cross)
        {
            if (!buildSchema.TryGetValue(buildCol!, out int buildColIdx))
                throw new InvalidOperationException($"Build join column '{buildCol}' not found in schema.");

            foreach (var row in buildRows)
            {
                var key = row[buildColIdx];
                if (key.IsNull) continue;

                if (!hashTable.TryGetValue(key, out var list))
                {
                    list = new RowSet(4); // Most buckets are small
                    hashTable[key] = list;
                }
                list.Add(row);
            }
        }

        // Probe
        int probeColIdx = -1;
        if (join.Kind != JoinType.Cross)
        {
            if (!probeSchema.TryGetValue(probeCol!, out probeColIdx))
                throw new InvalidOperationException($"Probe join column '{probeCol}' not found in schema.");
        }

        // Pre-build the null row for LEFT JOIN (reused across all unmatched probe rows)
        QueryValue[]? leftJoinNullRow = null;
        if (join.Kind == JoinType.Left)
        {
            leftJoinNullRow = new QueryValue[rightColumnCount];
            Array.Fill(leftJoinNullRow, QueryValue.Null);
        }

        // When reuseBuffer is true, the downstream will project (copy needed columns
        // into a new narrower array) before exposing the row. This makes the merged
        // array a scratch buffer: we allocate ONE and overwrite it on every yield.
        // Safe because yield return pauses until the consumer calls MoveNext(), and
        // ProjectRows reads the buffer BEFORE advancing the source iterator.
        QueryValue[]? scratch = reuseBuffer ? new QueryValue[mergedWidth] : null;

        foreach (var probeRow in probeRows)
        {
            if (join.Kind == JoinType.Cross)
            {
                foreach (var buildRow in buildRows)
                {
                    // Output is always [left columns, right columns]
                    yield return swapped
                        ? MergeRows(buildRow, probeRow, mergedWidth, leftColumnCount, scratch)
                        : MergeRows(probeRow, buildRow, mergedWidth, leftColumnCount, scratch);
                }
                continue;
            }

            var key = probeRow[probeColIdx];
            if (!key.IsNull && hashTable.TryGetValue(key, out var matches))
            {
                foreach (var buildRow in matches)
                {
                    // Maintain [left, right] column order regardless of build/probe swap
                    yield return swapped
                        ? MergeRows(buildRow, probeRow, mergedWidth, leftColumnCount, scratch)
                        : MergeRows(probeRow, buildRow, mergedWidth, leftColumnCount, scratch);
                }
            }
            else if (join.Kind == JoinType.Left)
            {
                yield return MergeRows(probeRow, leftJoinNullRow!, mergedWidth, leftColumnCount, scratch);
            }
        }
    }

    private static QueryValue[] MergeRows(QueryValue[] left, QueryValue[] right, int mergedWidth, int leftLen, QueryValue[]? scratch = null)
    {
        var combined = scratch ?? new QueryValue[mergedWidth];
        left.AsSpan(0, leftLen).CopyTo(combined);
        right.AsSpan().CopyTo(combined.AsSpan(leftLen));
        return combined;
    }

    /// <summary>
    /// Streaming LIMIT/OFFSET — avoids materializing the entire join result into a List.
    /// </summary>
    private static IEnumerable<QueryValue[]> StreamingLimitOffset(
        IEnumerable<QueryValue[]> source, int offset, int? limit)
    {
        int skipped = 0;
        int emitted = 0;
        int max = limit ?? int.MaxValue;

        foreach (var row in source)
        {
            if (skipped < offset) { skipped++; continue; }
            if (emitted >= max) yield break;
            yield return row;
            emitted++;
        }
    }

    private static (IEnumerable<QueryValue[]> Rows, Dictionary<string, int> Schema) MaterializeRows(
        SharcDatabase db, QueryIntent intent, IReadOnlyDictionary<string, object>? parameters, string prefix,
        CoteMap? coteResults = null)
    {
        // Pre-materialized Cote data: use it directly instead of reading from disk
        if (coteResults != null && coteResults.TryGetValue(intent.TableName, out var coteData))
        {
            var schema = new Dictionary<string, int>(coteData.Columns.Length, StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < coteData.Columns.Length; i++)
                schema[string.Concat(prefix, ".", coteData.Columns[i])] = i;

            return (coteData.Rows, schema);
        }

        var reader = db.CreateReaderFromIntent(intent, parameters);
        var fieldCount = reader.FieldCount;

        var schema2 = new Dictionary<string, int>(fieldCount, StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < fieldCount; i++)
        {
            string name = reader.GetColumnName(i);
            schema2[string.Concat(prefix, ".", name)] = i;
        }

        IEnumerable<QueryValue[]> Iterator()
        {
            using (reader)
            {
                while (reader.Read())
                {
                    var row = new QueryValue[fieldCount];
                    for (int i = 0; i < fieldCount; i++)
                    {
                        row[i] = ReadValue(reader, i);
                    }
                    yield return row;
                }
            }
        }

        return (Iterator(), schema2);
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

    private static QueryIntent CloneWithPushdown(QueryIntent intent, TableExecutionNode node)
    {
        var requiredColumns = node.RequiredColumns;
        var columns = new List<string>(requiredColumns.Count);
        foreach (var c in requiredColumns)
            columns.Add(string.Concat(node.Alias, ".", c));

        return new QueryIntent
        {
            TableName = intent.TableName,
            TableRecordId = intent.TableRecordId,
            TableAlias = node.Alias,
            Columns = columns,
            Filter = node.PushdownFilter,
            OrderBy = null,
            Limit = null,
            Offset = null,
            IsDistinct = false,
            Aggregates = null,
            GroupBy = null,
            HavingFilter = null
        };
    }

    private static JoinExecutionPlan BuildPlan(QueryIntent intent)
    {
        var plan = new JoinExecutionPlan();
        
        // Register all tables
        var primaryAlias = intent.TableAlias ?? intent.TableName;
        plan.Nodes[primaryAlias] = new TableExecutionNode { TableName = intent.TableName, Alias = primaryAlias };
        if (intent.Joins != null)
        {
            foreach (var j in intent.Joins)
            {
                var alias = j.TableAlias ?? j.TableName;
                plan.Nodes[alias] = new TableExecutionNode { TableName = j.TableName, Alias = alias };
            }
        }

        // 1. Collect required columns
        CollectRequiredColumns(intent, plan);

        // 2. Split Filter (Filter pushdown)
        if (intent.Filter.HasValue)
        {
            SplitFilters(intent.Filter.Value, plan);
        }

        return plan;
    }

    private static void CollectRequiredColumns(QueryIntent intent, JoinExecutionPlan plan)
    {
        void Record(string? raw)
        {
            if (string.IsNullOrEmpty(raw)) return;
            int dotIdx = raw.IndexOf('.');
            if (dotIdx > 0)
            {
                string alias = raw[..dotIdx];
                string column = raw[(dotIdx + 1)..];
                if (plan.Nodes.TryGetValue(alias, out var node))
                    node.RequiredColumns.Add(column);
            }
            else
            {
                // Unqualified: assign to primary table if possible
                var primaryAlias = intent.TableAlias ?? intent.TableName;
                plan.Nodes[primaryAlias].RequiredColumns.Add(raw);
            }
        }

        if (intent.Columns != null)
            foreach (var col in intent.Columns) Record(col);

        if (intent.Joins != null)
        {
            foreach (var j in intent.Joins)
            {
                Record(j.LeftColumn);
                Record(j.RightColumn);
            }
        }

        if (intent.OrderBy != null)
            foreach (var o in intent.OrderBy) Record(o.ColumnName);

        if (intent.GroupBy != null)
            foreach (var g in intent.GroupBy) Record(g);

        if (intent.Filter.HasValue)
        {
            foreach (var node in intent.Filter.Value.Nodes)
                Record(node.ColumnName);
        }
    }

    /// <summary>
    /// Decomposes a WHERE predicate into per-table push-down filters.
    /// Walks the AND tree and, for each leaf or sub-tree that references only a single
    /// table alias, pushes it down to that table's node in the execution plan.
    /// Predicates that span multiple tables remain as post-join residual filters.
    /// </summary>
    private static void SplitFilters(PredicateIntent filter, JoinExecutionPlan plan)
    {
        var nodes = filter.Nodes;
        Decompose(filter.RootIndex);

        void Decompose(int index)
        {
            var node = nodes[index];
            if (node.Op == IntentOp.And)
            {
                Decompose(node.LeftIndex);
                Decompose(node.RightIndex);
                return;
            }

            // Try to push this sub-tree
            string? targetAlias = GetSoleTargetAlias(index, nodes);
            if (targetAlias != null && plan.Nodes.TryGetValue(targetAlias, out var tableNode))
            {
                // Rewrite sub-tree into pushdown nodes
                int newRoot = CopySubtree(index, nodes, tableNode.PushdownNodes);
                if (tableNode.PushdownRootIndex == -1)
                {
                    tableNode.PushdownRootIndex = newRoot;
                }
                else
                {
                    // Wrap existing root with AND
                    int oldRoot = tableNode.PushdownRootIndex;
                    tableNode.PushdownNodes.Add(new Sharc.Query.Intent.PredicateNode 
                    { 
                        Op = IntentOp.And, 
                        LeftIndex = oldRoot, 
                        RightIndex = newRoot 
                    });
                    tableNode.PushdownRootIndex = tableNode.PushdownNodes.Count - 1;
                }
            }
            else
            {
                // Remains as residual
                int newRoot = CopySubtree(index, nodes, plan.ResidualNodes);
                if (plan.ResidualRootIndex == -1)
                {
                    plan.ResidualRootIndex = newRoot;
                }
                else
                {
                    plan.ResidualNodes.Add(new Sharc.Query.Intent.PredicateNode 
                    { 
                        Op = IntentOp.And, 
                        LeftIndex = plan.ResidualRootIndex, 
                        RightIndex = newRoot 
                    });
                    plan.ResidualRootIndex = plan.ResidualNodes.Count - 1;
                }
            }
        }
    }

    private static string? GetSoleTargetAlias(int index, Sharc.Query.Intent.PredicateNode[] nodes)
    {
        string? soleAlias = null;
        var stack = new Stack<int>();
        stack.Push(index);
        while (stack.Count > 0)
        {
            var node = nodes[stack.Pop()];
            if (node.ColumnName != null)
            {
                int dotIdx = node.ColumnName.IndexOf('.');
                string? alias = dotIdx > 0 ? node.ColumnName[..dotIdx] : null; // Assume null means primary, but better be explicit
                if (alias == null) return null; // Mixed or unqualified is risky here

                if (soleAlias == null) soleAlias = alias;
                else if (!string.Equals(soleAlias, alias, StringComparison.OrdinalIgnoreCase)) return null;
            }
            if (node.LeftIndex != -1) stack.Push(node.LeftIndex);
            if (node.RightIndex != -1) stack.Push(node.RightIndex);
        }
        return soleAlias;
    }

    private static int CopySubtree(int index, Sharc.Query.Intent.PredicateNode[] source, List<Sharc.Query.Intent.PredicateNode> target)
    {
        var node = source[index];
        int left = -1, right = -1;
        if (node.LeftIndex != -1) left = CopySubtree(node.LeftIndex, source, target);
        if (node.RightIndex != -1) right = CopySubtree(node.RightIndex, source, target);

        target.Add(new Sharc.Query.Intent.PredicateNode
        {
            Op = node.Op,
            ColumnName = node.ColumnName,
            Value = node.Value,
            HighValue = node.HighValue,
            LeftIndex = left,
            RightIndex = right
        });
        return target.Count - 1;
    }

    private static IEnumerable<QueryValue[]> FilterRows(IEnumerable<QueryValue[]> rows, Dictionary<string, int> schema, PredicateIntent filter)
    {
        foreach (var row in rows)
        {
            if (Evaluate(filter, row, schema))
                yield return row;
        }
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

    /// <summary>
    /// Builds a column name array ordered by schema ordinal, without LINQ allocation.
    /// </summary>
    private static string[] BuildColumnNames(Dictionary<string, int> schema)
    {
        var columns = new string[schema.Count];
        foreach (var kv in schema)
            columns[kv.Value] = kv.Key;
        return columns;
    }

    private static IEnumerable<QueryValue[]> ProjectRows(IEnumerable<QueryValue[]> rows, Dictionary<string, int> schema, IReadOnlyList<string> columns)
    {
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
            yield return newRow;
        }
    }
}
