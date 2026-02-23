// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Xunit;

namespace Sharc.Tests.Query;

public class IndexSetTests
{
    private static Fingerprint128 Fp(ulong lo, uint guard, ushort len, ushort tag) =>
        new(lo, guard, len, tag);

    // ─── Add / Contains basics ──────────────────────────────────

    [Fact]
    public void Add_NewEntry_ReturnsTrue()
    {
        using var set = IndexSet.Rent();
        Assert.True(set.Add(Fp(100, 1, 10, 1)));
    }

    [Fact]
    public void Add_Duplicate_ReturnsFalse()
    {
        using var set = IndexSet.Rent();
        var fp = Fp(100, 1, 10, 1);
        Assert.True(set.Add(fp));
        Assert.False(set.Add(fp));
    }

    [Fact]
    public void Contains_AfterAdd_ReturnsTrue()
    {
        using var set = IndexSet.Rent();
        var fp = Fp(200, 2, 20, 2);
        set.Add(fp);
        Assert.True(set.Contains(fp));
    }

    [Fact]
    public void Contains_NotAdded_ReturnsFalse()
    {
        using var set = IndexSet.Rent();
        set.Add(Fp(100, 1, 10, 1));
        Assert.False(set.Contains(Fp(200, 2, 20, 2)));
    }

    [Fact]
    public void Contains_EmptySet_ReturnsFalse()
    {
        using var set = IndexSet.Rent();
        Assert.False(set.Contains(Fp(100, 1, 10, 1)));
    }

    // ─── Sentinel (0,0) handling ────────────────────────────────

    [Fact]
    public void Add_ZeroZeroEntry_HandledCorrectly()
    {
        using var set = IndexSet.Rent();
        var zero = Fp(0, 0, 0, 0);
        Assert.True(set.Add(zero));
        Assert.True(set.Contains(zero));
        Assert.False(set.Add(zero)); // duplicate
    }

    [Fact]
    public void Add_ZeroZeroAndNonZero_DistinctEntries()
    {
        using var set = IndexSet.Rent();
        var zero = Fp(0, 0, 0, 0);
        var nonZero = Fp(42, 1, 5, 3);
        Assert.True(set.Add(zero));
        Assert.True(set.Add(nonZero));
        Assert.True(set.Contains(zero));
        Assert.True(set.Contains(nonZero));
    }

    // ─── Growth / many entries ──────────────────────────────────

    [Fact]
    public void Add_ManyEntries_AllRetrievable()
    {
        using var set = IndexSet.Rent();
        const int count = 500;
        for (int i = 1; i <= count; i++)
            Assert.True(set.Add(Fp((ulong)i * 7, (uint)i, (ushort)(i % 65535), (ushort)(i & 0xFF))));

        for (int i = 1; i <= count; i++)
            Assert.True(set.Contains(Fp((ulong)i * 7, (uint)i, (ushort)(i % 65535), (ushort)(i & 0xFF))));

        Assert.False(set.Contains(Fp(999999, 0, 0, 0)));
    }

    [Fact]
    public void Add_5000Entries_NoInfiniteLoop()
    {
        using var set = IndexSet.Rent();
        for (int i = 1; i <= 5000; i++)
            set.Add(Fp((ulong)i, (uint)(i >> 16), (ushort)i, (ushort)(i & 0xF)));

        // Verify with same formula used during Add
        Assert.True(set.Contains(Fp(1, (uint)(1 >> 16), (ushort)1, (ushort)(1 & 0xF))));
        Assert.True(set.Contains(Fp(5000, (uint)(5000 >> 16), (ushort)5000, (ushort)(5000 & 0xF))));
    }

    // ─── Duplicate detection at scale ───────────────────────────

    [Fact]
    public void Add_DuplicatesAtScale_CorrectCount()
    {
        using var set = IndexSet.Rent();
        int added = 0;
        for (int i = 0; i < 200; i++)
        {
            if (set.Add(Fp((ulong)(i + 1) * 13, 1, 1, 1))) added++;
            if (set.Add(Fp((ulong)(i + 1) * 13, 1, 1, 1))) added++;
        }
        Assert.Equal(200, added);
    }

    // ─── Rent / Dispose pooling ─────────────────────────────────

    [Fact]
    public void Rent_AfterDispose_ClearsOldData()
    {
        var set1 = IndexSet.Rent();
        set1.Add(Fp(42, 1, 1, 1));
        set1.Dispose();

        var set2 = IndexSet.Rent();
        Assert.False(set2.Contains(Fp(42, 1, 1, 1)));
        set2.Dispose();
    }

    [Fact]
    public void Dispose_CalledTwice_NoException()
    {
        var set = IndexSet.Rent();
        set.Add(Fp(1, 1, 1, 1));
        set.Dispose();
        set.Dispose();
    }

    // ─── Collision resistance (same Lo, different Hi) ───────────

    [Fact]
    public void Add_SameLoButDifferentHi_BothStored()
    {
        using var set = IndexSet.Rent();
        var fp1 = Fp(100, 1, 10, 1);
        var fp2 = Fp(100, 2, 20, 2);
        Assert.True(set.Add(fp1));
        Assert.True(set.Add(fp2));
        Assert.True(set.Contains(fp1));
        Assert.True(set.Contains(fp2));
    }

    [Fact]
    public void Add_SameHiButDifferentLo_BothStored()
    {
        using var set = IndexSet.Rent();
        var fp1 = Fp(100, 1, 10, 1);
        var fp2 = Fp(200, 1, 10, 1);
        Assert.True(set.Add(fp1));
        Assert.True(set.Add(fp2));
        Assert.True(set.Contains(fp1));
        Assert.True(set.Contains(fp2));
    }
}
