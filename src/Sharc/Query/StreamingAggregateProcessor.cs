// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Query.Intent;

namespace Sharc.Query;

/// <summary>
/// Coordinates streaming aggregation: feeds rows one-at-a-time into
/// <see cref="StreamingAggregator"/>, then applies optional ORDER BY and LIMIT/OFFSET.
/// Uses O(G) memory where G = number of groups, not O(N) for the full result set.
/// </summary>
internal static class StreamingAggregateProcessor
{
    internal static SharcDataReader Apply(
        SharcDataReader source, QueryIntent intent,
        bool needsSort, bool needsLimit)
    {
        int fieldCount = source.FieldCount;
        var columnNames = source.GetColumnNames();

        var aggregator = new StreamingAggregator(
            columnNames,
            intent.Aggregates!,
            intent.GroupBy,
            intent.Columns);

        // Reuse a single buffer — StreamingAggregator copies all values it needs
        // (group key values on new groups, numeric accumulators by value).
        var buffer = new QueryValue[fieldCount];
        while (source.Read())
        {
            for (int i = 0; i < fieldCount; i++)
                buffer[i] = QueryValueOps.MaterializeColumn(source, i);
            aggregator.AccumulateRow(buffer);
        }
        source.Dispose();

        var (rows, outColumnNames) = aggregator.Finalize();

        if (needsSort)
            QueryPostProcessor.ApplyOrderBy(rows, intent.OrderBy!, outColumnNames);

        if (needsLimit)
            rows = QueryPostProcessor.ApplyLimitOffset(rows, intent.Limit, intent.Offset);

        return new SharcDataReader(rows.ToArray(), outColumnNames);
    }
}
