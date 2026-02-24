// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Query;
using Sharc.Query.Execution;
using Xunit;

namespace Sharc.Tests.Query;

/// <summary>
/// Tests for <see cref="OpenAddressHashTable{TKey}"/> — ArrayPool-backed open-addressing
/// hash table with backward-shift deletion for Tier III destructive probe.
/// </summary>
public sealed class OpenAddressHashTableTests
{
    [Fact]
    public void Add_ThenTryGet_ReturnsValue()
    {
        using var table = new OpenAddressHashTable<int>(16);
        table.Add(42, 0);

        Assert.True(table.TryGetFirst(42, out int index));
        Assert.Equal(0, index);
    }

    [Fact]
    public void TryGetFirst_MissingKey_ReturnsFalse()
    {
        using var table = new OpenAddressHashTable<int>(16);
        table.Add(1, 0);

        Assert.False(table.TryGetFirst(999, out _));
    }

    [Fact]
    public void Add_DuplicateKeys_AllRetrievable()
    {
        using var table = new OpenAddressHashTable<int>(32);
        table.Add(10, 0);
        table.Add(10, 1);
        table.Add(10, 2);

        var indices = new List<int>();
        table.GetAll(10, indices);

        Assert.Equal(3, indices.Count);
        Assert.Contains(0, indices);
        Assert.Contains(1, indices);
        Assert.Contains(2, indices);
    }

    [Fact]
    public void Remove_SingleKey_NotFoundAfter()
    {
        using var table = new OpenAddressHashTable<int>(16);
        table.Add(42, 0);

        bool removed = table.Remove(42);
        Assert.True(removed);
        Assert.False(table.TryGetFirst(42, out _));
    }

    [Fact]
    public void Remove_OneOfDuplicates_OthersRemain()
    {
        using var table = new OpenAddressHashTable<int>(32);
        table.Add(10, 0);
        table.Add(10, 1);
        table.Add(10, 2);

        // Remove one entry
        table.Remove(10);

        var indices = new List<int>();
        table.GetAll(10, indices);
        Assert.Equal(2, indices.Count);
    }

    [Fact]
    public void RemoveAll_DuplicateKey_RemovesAll()
    {
        using var table = new OpenAddressHashTable<int>(32);
        table.Add(10, 0);
        table.Add(10, 1);
        table.Add(10, 2);
        table.Add(20, 3);

        int removed = table.RemoveAll(10);
        Assert.Equal(3, removed);
        Assert.False(table.TryGetFirst(10, out _));

        // Other keys unaffected
        Assert.True(table.TryGetFirst(20, out int idx));
        Assert.Equal(3, idx);
    }

    [Fact]
    public void BackwardShiftDeletion_PreservesProbeChain()
    {
        // Insert keys that will collide (same hash bucket),
        // then delete the first one — backward shift should keep the chain intact.
        using var table = new OpenAddressHashTable<int>(8);

        // These will likely collide in a small table
        table.Add(0, 0);
        table.Add(8, 1);  // hash(8) % 8 == hash(0) % 8 for many hash functions
        table.Add(16, 2);

        // Remove the first one in the chain
        table.Remove(0);

        // The other entries should still be found
        Assert.True(table.TryGetFirst(8, out int idx1));
        Assert.Equal(1, idx1);
        Assert.True(table.TryGetFirst(16, out int idx2));
        Assert.Equal(2, idx2);
    }

    [Fact]
    public void Count_TracksInsertionsAndRemovals()
    {
        using var table = new OpenAddressHashTable<int>(32);
        Assert.Equal(0, table.Count);

        table.Add(1, 0);
        table.Add(2, 1);
        table.Add(3, 2);
        Assert.Equal(3, table.Count);

        table.Remove(2);
        Assert.Equal(2, table.Count);
    }

    [Fact]
    public void StringKeys_WorkCorrectly()
    {
        using var table = new OpenAddressHashTable<string>(16);
        table.Add("hello", 0);
        table.Add("world", 1);

        Assert.True(table.TryGetFirst("hello", out int idx));
        Assert.Equal(0, idx);
        Assert.True(table.TryGetFirst("world", out int idx2));
        Assert.Equal(1, idx2);
        Assert.False(table.TryGetFirst("missing", out _));
    }

    [Fact]
    public void HighLoadFactor_StillWorks()
    {
        // Fill to ~75% capacity
        using var table = new OpenAddressHashTable<int>(16);
        for (int i = 0; i < 12; i++)
            table.Add(i * 100, i);

        for (int i = 0; i < 12; i++)
        {
            Assert.True(table.TryGetFirst(i * 100, out int idx));
            Assert.Equal(i, idx);
        }
    }

    [Fact]
    public void Empty_TryGet_ReturnsFalse()
    {
        using var table = new OpenAddressHashTable<int>(16);
        Assert.False(table.TryGetFirst(0, out _));
    }

    [Fact]
    public void Dispose_IsIdempotent()
    {
        var table = new OpenAddressHashTable<int>(16);
        table.Add(1, 0);
        table.Dispose();
        table.Dispose(); // should not throw
    }

    [Fact]
    public void RemoveAll_EmptyKey_ReturnsZero()
    {
        using var table = new OpenAddressHashTable<int>(16);
        Assert.Equal(0, table.RemoveAll(42));
    }

    [Fact]
    public void DrainThenRemove_Pattern_Works()
    {
        // This tests the drain-then-remove protocol from the paper:
        // 1. Enumerate all matches (read-only)
        // 2. Batch-remove all entries for that key
        using var table = new OpenAddressHashTable<int>(64);

        // 5 rows with key=10, 3 rows with key=20
        for (int i = 0; i < 5; i++)
            table.Add(10, i);
        for (int i = 0; i < 3; i++)
            table.Add(20, 100 + i);

        Assert.Equal(8, table.Count);

        // Drain key=10
        var drained = new List<int>();
        table.GetAll(10, drained);
        Assert.Equal(5, drained.Count);

        // Remove key=10
        int removed = table.RemoveAll(10);
        Assert.Equal(5, removed);
        Assert.Equal(3, table.Count);

        // key=20 still intact
        var remaining = new List<int>();
        table.GetAll(20, remaining);
        Assert.Equal(3, remaining.Count);
    }

    [Fact]
    public void ResidualEntries_AreUnmatchedBuildRows()
    {
        // After draining all probe-matched keys, remaining entries are unmatched build rows
        using var table = new OpenAddressHashTable<int>(64);
        table.Add(1, 0);
        table.Add(2, 1);
        table.Add(3, 2);
        table.Add(4, 3);

        // Simulate probe finding keys 1 and 3
        table.RemoveAll(1);
        table.RemoveAll(3);

        // Residual = keys 2 and 4 (unmatched build rows)
        Assert.Equal(2, table.Count);
        Assert.True(table.TryGetFirst(2, out _));
        Assert.True(table.TryGetFirst(4, out _));
    }
}
