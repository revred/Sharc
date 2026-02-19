// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

namespace Sharc.Query;

/// <summary>
/// Streaming set operations (UNION, INTERSECT, EXCEPT) that avoid full
/// materialization of both sides. Rows are read directly from B-tree cursors,
/// checked against a HashSet, and only surviving rows are allocated and kept.
/// Non-surviving rows are recycled via a spare-array pattern (zero allocation
/// for rejected rows after the first pass).
/// </summary>
internal static class StreamingSetOpProcessor
{
    /// <summary>
    /// Streaming UNION: reads both sides from cursors, deduplicates via HashSet,
    /// outputs unique rows. Avoids materializing either side into an intermediate list.
    /// </summary>
    internal static MaterializedResultSet StreamingUnion(
        SharcDataReader leftReader,
        SharcDataReader rightReader)
    {
        int fieldCount = leftReader.FieldCount;
        var columnNames = leftReader.GetColumnNames();
        var comparer = new QueryValueOps.QvRowEqualityComparer(fieldCount);
        var seen = new HashSet<QueryValue[]>(comparer);
        var result = new RowSet();
        QueryValue[]? spare = null;

        StreamInto(leftReader, fieldCount, seen, result, ref spare);
        StreamInto(rightReader, fieldCount, seen, result, ref spare);

        return new MaterializedResultSet(result, columnNames);
    }

    /// <summary>
    /// Streaming INTERSECT: materializes the right side into a HashSet,
    /// then streams the left side — only rows present in both sides survive.
    /// </summary>
    internal static MaterializedResultSet StreamingIntersect(
        SharcDataReader leftReader,
        SharcDataReader rightReader)
    {
        int fieldCount = leftReader.FieldCount;
        var columnNames = leftReader.GetColumnNames();
        var comparer = new QueryValueOps.QvRowEqualityComparer(fieldCount);

        // Materialize right side directly into a HashSet (no intermediate list)
        var rightSet = MaterializeIntoHashSet(rightReader, fieldCount, comparer);

        // Stream left side — only keep rows that exist in rightSet
        var seen = new HashSet<QueryValue[]>(comparer);
        var result = new RowSet();
        QueryValue[]? spare = null;

        while (leftReader.Read())
        {
            var row = spare ?? new QueryValue[fieldCount];
            spare = null;
            MaterializeRow(leftReader, row, fieldCount);

            if (rightSet.Contains(row) && seen.Add(row))
                result.Add(row);
            else
                spare = row; // recycle — this array is not referenced
        }

        return new MaterializedResultSet(result, columnNames);
    }

    /// <summary>
    /// Streaming EXCEPT: materializes the right side into a HashSet,
    /// then streams the left side — only rows NOT present in the right side survive.
    /// </summary>
    internal static MaterializedResultSet StreamingExcept(
        SharcDataReader leftReader,
        SharcDataReader rightReader)
    {
        int fieldCount = leftReader.FieldCount;
        var columnNames = leftReader.GetColumnNames();
        var comparer = new QueryValueOps.QvRowEqualityComparer(fieldCount);

        // Materialize right side directly into a HashSet (no intermediate list)
        var rightSet = MaterializeIntoHashSet(rightReader, fieldCount, comparer);

        // Stream left side — only keep rows that do NOT exist in rightSet
        var seen = new HashSet<QueryValue[]>(comparer);
        var result = new RowSet();
        QueryValue[]? spare = null;

        while (leftReader.Read())
        {
            var row = spare ?? new QueryValue[fieldCount];
            spare = null;
            MaterializeRow(leftReader, row, fieldCount);

            if (!rightSet.Contains(row) && seen.Add(row))
                result.Add(row);
            else
                spare = row;
        }

        return new MaterializedResultSet(result, columnNames);
    }

    // ─── Helpers ──────────────────────────────────────────────────

    /// <summary>
    /// Streams rows from a reader into the seen HashSet and result list.
    /// Reuses spare arrays for duplicate rows (zero allocation for rejects).
    /// </summary>
    private static void StreamInto(
        SharcDataReader reader,
        int fieldCount,
        HashSet<QueryValue[]> seen,
        RowSet result,
        ref QueryValue[]? spare)
    {
        while (reader.Read())
        {
            var row = spare ?? new QueryValue[fieldCount];
            spare = null;
            MaterializeRow(reader, row, fieldCount);

            if (seen.Add(row))
                result.Add(row);
            else
                spare = row;
        }
    }

    /// <summary>
    /// Materializes all rows from a reader directly into a HashSet (no intermediate list).
    /// Reuses spare arrays for rows that are already in the set (duplicates within the side).
    /// </summary>
    private static HashSet<QueryValue[]> MaterializeIntoHashSet(
        SharcDataReader reader,
        int fieldCount,
        QueryValueOps.QvRowEqualityComparer comparer)
    {
        var set = new HashSet<QueryValue[]>(comparer);
        QueryValue[]? spare = null;

        while (reader.Read())
        {
            var row = spare ?? new QueryValue[fieldCount];
            spare = null;
            MaterializeRow(reader, row, fieldCount);

            if (!set.Add(row))
                spare = row; // duplicate within right side — recycle
        }

        return set;
    }

    /// <summary>
    /// Fills a pre-allocated row array from the current reader position.
    /// </summary>
    private static void MaterializeRow(SharcDataReader reader, QueryValue[] row, int fieldCount)
    {
        for (int i = 0; i < fieldCount; i++)
            row[i] = QueryValueOps.MaterializeColumn(reader, i);
    }
}
