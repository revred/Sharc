// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using System.Buffers.Binary;
using Sharc.Core.IO;
using Xunit;

namespace Sharc.Tests.IO;

/// <summary>
/// Stress tests for <see cref="FreelistManager"/> — verifies push/pop round-trip
/// integrity, trunk overflow chains, heavy churn cycles, and LIFO ordering under
/// varied page counts.
/// </summary>
public sealed class FreelistManagerStressTests
{
    private const int PageSize = 4096;
    private const int MaxLeavesPerTrunk = (PageSize - 8) / 4; // 1022

    private static byte[] CreateValidHeader(int pageCount)
    {
        var data = new byte[PageSize * pageCount];
        "SQLite format 3\0"u8.CopyTo(data);
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(16), PageSize);
        data[18] = 1; data[19] = 1;
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(28), (uint)pageCount);
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(40), 1);
        return data;
    }

    [Fact]
    public void PushThenPopAll_ReturnsAllPages()
    {
        var data = CreateValidHeader(100);
        var source = new MemoryPageSource(data);
        var mgr = new FreelistManager(source, PageSize);
        mgr.Initialize(0, 0);

        var pushed = new HashSet<uint>();
        for (uint i = 10; i <= 50; i++)
        {
            mgr.PushFreePage(i);
            pushed.Add(i);
        }

        Assert.Equal(41, mgr.FreelistPageCount);

        var popped = new HashSet<uint>();
        while (mgr.HasFreePages)
        {
            uint page = mgr.PopFreePage();
            Assert.NotEqual(0u, page);
            Assert.True(popped.Add(page), $"Page {page} popped twice");
        }

        Assert.Equal(pushed, popped);
        Assert.Equal(0, mgr.FreelistPageCount);
    }

    [Fact]
    public void PopFromEmpty_ReturnsZero()
    {
        var data = CreateValidHeader(2);
        var source = new MemoryPageSource(data);
        var mgr = new FreelistManager(source, PageSize);
        mgr.Initialize(0, 0);

        Assert.Equal(0u, mgr.PopFreePage());
        Assert.False(mgr.HasFreePages);
    }

    [Fact]
    public void PushPop_LIFOWithinTrunk()
    {
        var data = CreateValidHeader(20);
        var source = new MemoryPageSource(data);
        var mgr = new FreelistManager(source, PageSize);
        mgr.Initialize(0, 0);

        // Push 3 pages: 5 becomes trunk, 6 and 7 become leaves
        mgr.PushFreePage(5);
        mgr.PushFreePage(6);
        mgr.PushFreePage(7);

        // LIFO: pop leaves last-to-first, then trunk
        Assert.Equal(7u, mgr.PopFreePage()); // last leaf
        Assert.Equal(6u, mgr.PopFreePage()); // first leaf
        Assert.Equal(5u, mgr.PopFreePage()); // trunk itself
    }

    [Fact]
    public void TrunkOverflow_CreatesNewTrunk()
    {
        // Fill a trunk beyond MaxLeavesPerTrunk to force a new trunk
        int pagePoolSize = MaxLeavesPerTrunk + 10;
        var data = CreateValidHeader(pagePoolSize + 100);
        var source = new MemoryPageSource(data);
        var mgr = new FreelistManager(source, PageSize);
        mgr.Initialize(0, 0);

        for (uint i = 10; i < 10 + pagePoolSize; i++)
            mgr.PushFreePage(i);

        // Should have 1022 + 10 pages, across 2 trunks
        Assert.Equal(pagePoolSize, mgr.FreelistPageCount);
        Assert.True(mgr.HasFreePages);

        // Pop all — every page should be recovered exactly once
        var popped = new HashSet<uint>();
        while (mgr.HasFreePages)
        {
            uint page = mgr.PopFreePage();
            Assert.True(popped.Add(page), $"Page {page} returned twice");
        }

        Assert.Equal(pagePoolSize, popped.Count);
    }

    [Fact]
    public void HeavyChurn_InterleavedPushPop_ConsistentCount()
    {
        var data = CreateValidHeader(200);
        var source = new MemoryPageSource(data);
        var mgr = new FreelistManager(source, PageSize);
        mgr.Initialize(0, 0);

        // Push 50 pages
        for (uint i = 10; i < 60; i++)
            mgr.PushFreePage(i);

        // Pop 20
        for (int i = 0; i < 20; i++)
            mgr.PopFreePage();

        Assert.Equal(30, mgr.FreelistPageCount);

        // Push 30 more (use pages 100-129 to avoid conflicts)
        for (uint i = 100; i < 130; i++)
            mgr.PushFreePage(i);

        Assert.Equal(60, mgr.FreelistPageCount);

        // Pop all
        var popped = new HashSet<uint>();
        while (mgr.HasFreePages)
        {
            uint page = mgr.PopFreePage();
            Assert.True(popped.Add(page));
        }

        Assert.Equal(60, popped.Count);
    }

    [Fact]
    public void SinglePagePushPop_TrunkItselfReturned()
    {
        var data = CreateValidHeader(20);
        var source = new MemoryPageSource(data);
        var mgr = new FreelistManager(source, PageSize);
        mgr.Initialize(0, 0);

        mgr.PushFreePage(7);
        Assert.Equal(1, mgr.FreelistPageCount);
        Assert.True(mgr.HasFreePages);

        uint page = mgr.PopFreePage();
        Assert.Equal(7u, page);
        Assert.Equal(0, mgr.FreelistPageCount);
        Assert.False(mgr.HasFreePages);
    }

    [Fact]
    public void FirstTrunkPage_UpdatesOnPush()
    {
        var data = CreateValidHeader(20);
        var source = new MemoryPageSource(data);
        var mgr = new FreelistManager(source, PageSize);
        mgr.Initialize(0, 0);

        Assert.Equal(0u, mgr.FirstTrunkPage);

        mgr.PushFreePage(5);
        Assert.Equal(5u, mgr.FirstTrunkPage);
    }

    [Fact]
    public void MultipleTrunks_PopExhaustsFirstTrunkThenAdvances()
    {
        var data = CreateValidHeader(50);
        var source = new MemoryPageSource(data);
        var mgr = new FreelistManager(source, PageSize);
        mgr.Initialize(0, 0);

        // Push 3 pages: page 10 = trunk, pages 11 & 12 = leaves
        mgr.PushFreePage(10);
        mgr.PushFreePage(11);
        mgr.PushFreePage(12);

        // Pop leaves first (LIFO), then trunk
        mgr.PopFreePage(); // 12
        mgr.PopFreePage(); // 11

        // Now trunk 10 has no leaves — popping it should return 10
        uint trunk = mgr.PopFreePage();
        Assert.Equal(10u, trunk);
        Assert.Equal(0u, mgr.FirstTrunkPage);
    }

    [Fact]
    public void PushPop_100Cycles_NoPageLoss()
    {
        var data = CreateValidHeader(300);
        var source = new MemoryPageSource(data);
        var mgr = new FreelistManager(source, PageSize);
        mgr.Initialize(0, 0);

        // 100 rounds: push 5 pages, pop 3
        uint nextPage = 10;
        int totalPushed = 0;
        int totalPopped = 0;

        for (int round = 0; round < 100; round++)
        {
            for (int j = 0; j < 5; j++)
            {
                mgr.PushFreePage(nextPage++);
                totalPushed++;
            }
            for (int j = 0; j < 3; j++)
            {
                uint page = mgr.PopFreePage();
                Assert.NotEqual(0u, page);
                totalPopped++;
            }
        }

        // Net: 500 pushed - 300 popped = 200 remaining
        Assert.Equal(totalPushed - totalPopped, mgr.FreelistPageCount);

        // Pop all remaining
        while (mgr.HasFreePages)
        {
            mgr.PopFreePage();
            totalPopped++;
        }

        Assert.Equal(totalPushed, totalPopped);
    }
}
