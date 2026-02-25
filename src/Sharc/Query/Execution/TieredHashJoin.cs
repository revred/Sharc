// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using System.Runtime.CompilerServices;

namespace Sharc.Query.Execution;

/// <summary>
/// Zero-allocation tiered FULL OUTER JOIN executor.
/// <list type="bullet">
/// <item><description>Tier I (≤256 build rows): stackalloc bit array, L1 cache resident.</description></item>
/// <item><description>Tier II (257–8,192 build rows): ArrayPool bit-packed tracker (Col&lt;bit&gt;).</description></item>
/// <item><description>Tier III (&gt;8,192 build rows): destructive probe with drain-then-remove.</description></item>
/// </list>
/// </summary>
internal static class TieredHashJoin
{
    /// <summary>
    /// Executes a FULL OUTER JOIN using tiered dispatch based on build-side cardinality.
    /// </summary>
    /// <param name="buildRows">Materialized build-side rows.</param>
    /// <param name="buildKeyIndex">Column index of the join key in build rows.</param>
    /// <param name="buildColumnCount">Number of columns in each build row.</param>
    /// <param name="probeRows">Probe-side rows (can be streaming).</param>
    /// <param name="probeKeyIndex">Column index of the join key in probe rows.</param>
    /// <param name="probeColumnCount">Number of columns in each probe row.</param>
    /// <param name="buildIsLeft">True if the build side maps to the left portion of the output.</param>
    /// <param name="reuseBuffer">
    /// When true, a single scratch buffer is yielded on every iteration instead of
    /// allocating a new <c>QueryValue[]</c> per row. The caller (typically ProjectRows)
    /// must consume the buffer before advancing the iterator. This eliminates all
    /// per-row allocations in the join emission path.
    /// </param>
    public static IEnumerable<QueryValue[]> Execute(
        List<QueryValue[]> buildRows, int buildKeyIndex, int buildColumnCount,
        IEnumerable<QueryValue[]> probeRows, int probeKeyIndex, int probeColumnCount,
        bool buildIsLeft, bool reuseBuffer = false)
    {
        var tier = JoinTier.Select(buildRows.Count);
        var desc = MergeDescriptor.Create(
            buildIsLeft ? buildColumnCount : probeColumnCount,
            buildIsLeft ? probeColumnCount : buildColumnCount,
            buildIsLeft);

        return tier switch
        {
            JoinTierKind.StackAlloc => ExecuteTierI(buildRows, buildKeyIndex, probeRows, probeKeyIndex, desc, reuseBuffer),
            JoinTierKind.Pooled => ExecuteTierII(buildRows, buildKeyIndex, probeRows, probeKeyIndex, desc, reuseBuffer),
            JoinTierKind.DestructiveProbe => ExecuteTierIII(buildRows, buildKeyIndex, probeRows, probeKeyIndex, desc, reuseBuffer),
            _ => ExecuteTierI(buildRows, buildKeyIndex, probeRows, probeKeyIndex, desc, reuseBuffer),
        };
    }

    /// <summary>
    /// Tier I: ≤256 build rows. PooledBitArray rents 32 bytes max from ArrayPool (fits in L1 cache).
    /// Cannot use stackalloc in an iterator method, but the ArrayPool rent is zero-GC.
    /// </summary>
    private static IEnumerable<QueryValue[]> ExecuteTierI(
        List<QueryValue[]> buildRows, int buildKeyIndex,
        IEnumerable<QueryValue[]> probeRows, int probeKeyIndex,
        MergeDescriptor desc, bool reuseBuffer)
    {
        var hashTable = BuildHashTable(buildRows, buildKeyIndex);
        return ExecuteWithBitArray(buildRows, buildKeyIndex, probeRows, probeKeyIndex, desc, hashTable, reuseBuffer);
    }

    /// <summary>
    /// Tier II: 257–8,192 build rows. PooledBitArray rents ≤1 KB from ArrayPool (fits in L2 cache).
    /// </summary>
    private static IEnumerable<QueryValue[]> ExecuteTierII(
        List<QueryValue[]> buildRows, int buildKeyIndex,
        IEnumerable<QueryValue[]> probeRows, int probeKeyIndex,
        MergeDescriptor desc, bool reuseBuffer)
    {
        var hashTable = BuildHashTable(buildRows, buildKeyIndex);
        return ExecuteWithBitArray(buildRows, buildKeyIndex, probeRows, probeKeyIndex, desc, hashTable, reuseBuffer);
    }

    /// <summary>
    /// Shared implementation for Tier I and II: uses PooledBitArray for matched-row tracking.
    /// </summary>
    private static IEnumerable<QueryValue[]> ExecuteWithBitArray(
        List<QueryValue[]> buildRows, int buildKeyIndex,
        IEnumerable<QueryValue[]> probeRows, int probeKeyIndex,
        MergeDescriptor desc,
        Dictionary<QueryValue, List<int>> hashTable,
        bool reuseBuffer)
    {
        var matched = PooledBitArray.Create(buildRows.Count);
        try
        {
            var output = new QueryValue[desc.MergedWidth];

            // Probe phase
            foreach (var probeRow in probeRows)
            {
                var key = probeRow[probeKeyIndex];
                if (!key.IsNull && hashTable.TryGetValue(key, out var matchIndices))
                {
                    foreach (var idx in matchIndices)
                    {
                        matched.Set(idx);
                        desc.MergeMatched(probeRow, buildRows[idx], output);
                        yield return reuseBuffer ? output : CopyRow(output);
                    }
                }
                else
                {
                    // Probe-unmatched
                    desc.EmitProbeUnmatched(probeRow, output);
                    yield return reuseBuffer ? output : CopyRow(output);
                }
            }

            // Build-unmatched phase: scan bit array
            for (int i = 0; i < buildRows.Count; i++)
            {
                if (!matched.Get(i))
                {
                    desc.EmitBuildUnmatched(buildRows[i], output);
                    yield return reuseBuffer ? output : CopyRow(output);
                }
            }
        }
        finally
        {
            matched.Dispose();
        }
    }

    /// <summary>
    /// Tier III: open-address hash table for &gt;8,192 build rows.
    /// Uses ArrayPool-backed open-addressing for better cache locality than Dictionary,
    /// with PooledBitArray for matched-row tracking (same as Tier I/II but with
    /// a more efficient hash table for large build sides).
    /// </summary>
    private static IEnumerable<QueryValue[]> ExecuteTierIII(
        List<QueryValue[]> buildRows, int buildKeyIndex,
        IEnumerable<QueryValue[]> probeRows, int probeKeyIndex,
        MergeDescriptor desc, bool reuseBuffer)
    {
        // Build open-address hash table: key → build-row index (cache-friendly)
        using var hashTable = new OpenAddressHashTable<QueryValue>(
            buildRows.Count, QueryValueKeyComparer.Instance);

        for (int i = 0; i < buildRows.Count; i++)
        {
            var key = buildRows[i][buildKeyIndex];
            if (!key.IsNull)
                hashTable.Add(key, i);
        }

        var matched = PooledBitArray.Create(buildRows.Count);
        try
        {
            var output = new QueryValue[desc.MergedWidth];
            var lookupBuffer = new List<int>(16);

            // Probe phase: read-only lookup, mark matches via bit array
            foreach (var probeRow in probeRows)
            {
                var key = probeRow[probeKeyIndex];
                if (!key.IsNull)
                {
                    lookupBuffer.Clear();
                    hashTable.GetAll(key, lookupBuffer);

                    if (lookupBuffer.Count > 0)
                    {
                        foreach (var idx in lookupBuffer)
                        {
                            matched.Set(idx);
                            desc.MergeMatched(probeRow, buildRows[idx], output);
                            yield return reuseBuffer ? output : CopyRow(output);
                        }
                    }
                    else
                    {
                        desc.EmitProbeUnmatched(probeRow, output);
                        yield return reuseBuffer ? output : CopyRow(output);
                    }
                }
                else
                {
                    desc.EmitProbeUnmatched(probeRow, output);
                    yield return reuseBuffer ? output : CopyRow(output);
                }
            }

            // Build-unmatched phase: scan bit array for unmatched rows
            for (int i = 0; i < buildRows.Count; i++)
            {
                if (!matched.Get(i))
                {
                    desc.EmitBuildUnmatched(buildRows[i], output);
                    yield return reuseBuffer ? output : CopyRow(output);
                }
            }
        }
        finally
        {
            matched.Dispose();
        }
    }

    /// <summary>
    /// Builds a Dictionary hash table mapping join keys to build-row indices.
    /// Skips NULL keys (SQL semantics: NULL ≠ NULL).
    /// </summary>
    private static Dictionary<QueryValue, List<int>> BuildHashTable(
        List<QueryValue[]> buildRows, int buildKeyIndex)
    {
        var hashTable = new Dictionary<QueryValue, List<int>>(
            buildRows.Count, QueryValueKeyComparer.Instance);

        for (int i = 0; i < buildRows.Count; i++)
        {
            var key = buildRows[i][buildKeyIndex];
            if (key.IsNull) continue;

            if (!hashTable.TryGetValue(key, out var list))
            {
                list = new List<int>(4);
                hashTable[key] = list;
            }
            list.Add(i);
        }

        return hashTable;
    }

    /// <summary>
    /// Creates a copy of the output row for yielding.
    /// The scratch buffer is reused across iterations.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static QueryValue[] CopyRow(QueryValue[] scratch)
    {
        var copy = new QueryValue[scratch.Length];
        scratch.AsSpan().CopyTo(copy);
        return copy;
    }

    /// <summary>
    /// Structural equality comparer for <see cref="QueryValue"/> keys in hash tables.
    /// Reuses the same equality/hash logic as the existing JoinExecutor.
    /// </summary>
    private sealed class QueryValueKeyComparer : IEqualityComparer<QueryValue>
    {
        internal static readonly QueryValueKeyComparer Instance = new();

        public bool Equals(QueryValue a, QueryValue b)
        {
            if (a.Type != b.Type)
            {
                if (a.Type == QueryValueType.Int64 && b.Type == QueryValueType.Double)
                    return (double)a.AsInt64() == b.AsDouble();
                if (a.Type == QueryValueType.Double && b.Type == QueryValueType.Int64)
                    return a.AsDouble() == (double)b.AsInt64();
                if (a.IsNull && b.IsNull) return true;
                return false;
            }

            return a.Type switch
            {
                QueryValueType.Null => true,
                QueryValueType.Int64 => a.AsInt64() == b.AsInt64(),
                QueryValueType.Double => a.AsDouble() == b.AsDouble(),
                QueryValueType.Text => string.Equals(a.AsString(), b.AsString(), StringComparison.Ordinal),
                _ => Equals(a.ObjectValue, b.ObjectValue),
            };
        }

        public int GetHashCode(QueryValue val)
        {
            var hash = new HashCode();
            QueryValueOps.AddToHash(ref hash, ref val);
            return hash.ToHashCode();
        }
    }
}
