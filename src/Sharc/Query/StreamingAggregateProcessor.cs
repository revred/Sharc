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

        // Identify which projected columns are group-key text columns.
        // For these, we pool strings by fingerprint to avoid 2500 string allocations
        // when there are only ~10 unique groups. Typical saving: ~75 KB.
        int groupKeyCount = intent.GroupBy?.Count ?? 0;
        bool hasTextGroupKeys = false;
        Dictionary<Fingerprint128, string>[]? textPools = null;
        bool[]? isTextGroupCol = null;

        if (groupKeyCount > 0)
        {
            isTextGroupCol = new bool[fieldCount];
            for (int i = 0; i < Math.Min(groupKeyCount, fieldCount); i++)
            {
                // Group-key columns appear first in the projected column list
                // (AggregateProjection puts them before aggregate source columns).
                // Use GetColumnType after the first Read to detect text columns.
            }
        }

        // Reuse a single buffer — StreamingAggregator copies all values it needs
        // (group key values on new groups, numeric accumulators by value).
        var buffer = new QueryValue[fieldCount];
        bool firstRow = true;
        while (source.Read())
        {
            // Detect text group-key columns on first row
            if (firstRow && groupKeyCount > 0)
            {
                firstRow = false;
                for (int i = 0; i < Math.Min(groupKeyCount, fieldCount); i++)
                {
                    if (source.GetColumnType(i) == SharcColumnType.Text)
                    {
                        isTextGroupCol![i] = true;
                        hasTextGroupKeys = true;
                    }
                }
                if (hasTextGroupKeys)
                {
                    textPools = new Dictionary<Fingerprint128, string>[fieldCount];
                    for (int i = 0; i < Math.Min(groupKeyCount, fieldCount); i++)
                    {
                        if (isTextGroupCol![i])
                            textPools[i] = new Dictionary<Fingerprint128, string>();
                    }
                }
            }
            else if (firstRow)
            {
                firstRow = false;
            }

            if (hasTextGroupKeys)
            {
                for (int i = 0; i < fieldCount; i++)
                {
                    if (isTextGroupCol![i])
                    {
                        // Zero-alloc 128-bit fingerprint from raw cursor bytes
                        var fp = source.GetColumnFingerprint(i);
                        if (!textPools![i]!.TryGetValue(fp, out var cached))
                        {
                            cached = source.GetString(i);
                            textPools[i]![fp] = cached;
                        }
                        buffer[i] = QueryValue.FromString(cached);
                    }
                    else
                    {
                        buffer[i] = QueryValueOps.MaterializeColumn(source, i);
                    }
                }
            }
            else
            {
                for (int i = 0; i < fieldCount; i++)
                    buffer[i] = QueryValueOps.MaterializeColumn(source, i);
            }
            aggregator.AccumulateRow(buffer);
        }
        source.Dispose();

        var (rows, outColumnNames) = aggregator.Finalize();

        if (needsSort)
            QueryPostProcessor.ApplyOrderBy(rows, intent.OrderBy!, outColumnNames);

        if (needsLimit)
            rows = QueryPostProcessor.ApplyLimitOffset(rows, intent.Limit, intent.Offset);

        return new SharcDataReader(rows, outColumnNames);
    }
}
