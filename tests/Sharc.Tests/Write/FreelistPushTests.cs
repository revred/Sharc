// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.


/*-------------------------------------------------------------------------------------------------!
  "Where the mind is free to imagine and the craft is guided by clarity, code awakens."            |

  A collaborative work shaped by Artificial Intelligence and curated with intent by Ram Revanur.
  Software here is treated not as static text, but as a living system designed to learn and evolve.
  Built on the belief that architecture and context often define outcomes before code is written.

  This file reflects an AI-aware, agentic, context-driven, and continuously evolving approach
  to modern engineering. If you seek to transform a traditional codebase into an adaptive,
  intelligence-guided system, you may find resonance in these patterns and principles.

  Subtle conversations often begin with a single message — or a prompt with the right context.
  https://www.linkedin.com/in/revodoc/

  Licensed under the MIT License — free for personal and commercial use.                           |
--------------------------------------------------------------------------------------------------*/

using Sharc.Core;
using Sharc.Core.IO;
using System.Buffers.Binary;
using Xunit;

namespace Sharc.Tests.Write;

/// <summary>
/// Tests for FreelistManager.PushFreePage — adding freed pages to the freelist.
/// </summary>
public sealed class FreelistPushTests
{
    private const int PageSize = 4096;

    private static MemoryPageSource CreateSource(int totalPages, uint firstTrunk = 0, int freelistCount = 0)
    {
        var data = new byte[PageSize * totalPages];

        "SQLite format 3\0"u8.CopyTo(data);
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(16), PageSize);
        data[18] = 1;
        data[19] = 1;
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(28), (uint)totalPages);
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(32), firstTrunk);
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(36), (uint)freelistCount);

        return new MemoryPageSource(data);
    }

    private static void WriteTrunkPage(MemoryPageSource source, uint pageNumber,
        uint nextTrunk, uint[] leafPages)
    {
        var page = new byte[PageSize];
        BinaryPrimitives.WriteUInt32BigEndian(page.AsSpan(0), nextTrunk);
        BinaryPrimitives.WriteUInt32BigEndian(page.AsSpan(4), (uint)leafPages.Length);
        for (int i = 0; i < leafPages.Length; i++)
        {
            BinaryPrimitives.WriteUInt32BigEndian(page.AsSpan(8 + i * 4), leafPages[i]);
        }
        source.WritePage(pageNumber, page);
    }

    private static (uint nextTrunk, int leafCount, uint[] leaves) ReadTrunkPage(
        ShadowPageSource source, uint pageNumber)
    {
        var page = source.GetPage(pageNumber);
        uint next = BinaryPrimitives.ReadUInt32BigEndian(page);
        int count = (int)BinaryPrimitives.ReadUInt32BigEndian(page[4..]);
        var leaves = new uint[count];
        for (int i = 0; i < count; i++)
            leaves[i] = BinaryPrimitives.ReadUInt32BigEndian(page[(8 + i * 4)..]);
        return (next, count, leaves);
    }

    [Fact]
    public void PushFreePage_EmptyFreelist_CreatesNewTrunkPage()
    {
        var source = CreateSource(5);
        var shadow = new ShadowPageSource(source);
        var mgr = new FreelistManager(shadow, PageSize);
        mgr.Initialize(0, 0);

        mgr.PushFreePage(3);

        Assert.Equal(1, mgr.FreelistPageCount);
        Assert.Equal(3u, mgr.FirstTrunkPage);

        // Verify trunk page was written correctly
        var (nextTrunk, leafCount, _) = ReadTrunkPage(shadow, 3);
        Assert.Equal(0u, nextTrunk);
        Assert.Equal(0, leafCount);
    }

    [Fact]
    public void PushFreePage_TrunkHasRoom_AddsAsLeaf()
    {
        // Existing trunk on page 3 with one leaf (page 5)
        var source = CreateSource(6, firstTrunk: 3, freelistCount: 2);
        WriteTrunkPage(source, 3, nextTrunk: 0, leafPages: [5]);

        var shadow = new ShadowPageSource(source);
        var mgr = new FreelistManager(shadow, PageSize);
        mgr.Initialize(3, 2);

        mgr.PushFreePage(4);

        Assert.Equal(3, mgr.FreelistPageCount);
        Assert.Equal(3u, mgr.FirstTrunkPage);

        // Verify leaf was added
        var (_, leafCount, leaves) = ReadTrunkPage(shadow, 3);
        Assert.Equal(2, leafCount);
        Assert.Equal(5u, leaves[0]);
        Assert.Equal(4u, leaves[1]);
    }

    [Fact]
    public void PushFreePage_TrunkFull_CreatesNewTrunk()
    {
        // Max leaves per trunk: (4096 - 8) / 4 = 1022
        int maxLeaves = (PageSize - 8) / 4;
        var source = CreateSource(maxLeaves + 5, firstTrunk: 3, freelistCount: maxLeaves + 1);

        // Fill trunk on page 3 to capacity
        var leaves = new uint[maxLeaves];
        for (int i = 0; i < maxLeaves; i++)
            leaves[i] = (uint)(10 + i);
        WriteTrunkPage(source, 3, nextTrunk: 0, leafPages: leaves);

        var shadow = new ShadowPageSource(source);
        var mgr = new FreelistManager(shadow, PageSize);
        mgr.Initialize(3, maxLeaves + 1);

        // Push one more page — should create a new trunk
        mgr.PushFreePage(4);

        Assert.Equal(maxLeaves + 2, mgr.FreelistPageCount);
        // New trunk is the pushed page (4), pointing to old trunk (3)
        Assert.Equal(4u, mgr.FirstTrunkPage);

        var (nextTrunk, leafCount, _) = ReadTrunkPage(shadow, 4);
        Assert.Equal(3u, nextTrunk);
        Assert.Equal(0, leafCount);
    }

    [Fact]
    public void PushFreePage_ThenPopFreePage_Roundtrips()
    {
        var source = CreateSource(5);
        var shadow = new ShadowPageSource(source);
        var mgr = new FreelistManager(shadow, PageSize);
        mgr.Initialize(0, 0);

        mgr.PushFreePage(3);
        mgr.PushFreePage(4);

        // Pop should return the last pushed leaf first
        uint first = mgr.PopFreePage();
        Assert.Equal(4u, first);

        // Then the trunk itself (page 3)
        uint second = mgr.PopFreePage();
        Assert.Equal(3u, second);

        Assert.False(mgr.HasFreePages);
    }

    [Fact]
    public void PushMultiplePages_AllRecoverable()
    {
        var source = CreateSource(10);
        var shadow = new ShadowPageSource(source);
        var mgr = new FreelistManager(shadow, PageSize);
        mgr.Initialize(0, 0);

        // Push 5 pages
        for (uint i = 3; i <= 7; i++)
            mgr.PushFreePage(i);

        Assert.Equal(5, mgr.FreelistPageCount);

        // Pop all and verify we get 5 pages back
        var recovered = new HashSet<uint>();
        for (int i = 0; i < 5; i++)
        {
            uint page = mgr.PopFreePage();
            Assert.NotEqual(0u, page);
            recovered.Add(page);
        }

        Assert.Equal(5, recovered.Count);
        Assert.False(mgr.HasFreePages);
    }

    [Fact]
    public void PushFreePage_FreelistCountIncreases()
    {
        var source = CreateSource(5);
        var shadow = new ShadowPageSource(source);
        var mgr = new FreelistManager(shadow, PageSize);
        mgr.Initialize(0, 0);

        Assert.Equal(0, mgr.FreelistPageCount);

        mgr.PushFreePage(3);
        Assert.Equal(1, mgr.FreelistPageCount);

        mgr.PushFreePage(4);
        Assert.Equal(2, mgr.FreelistPageCount);

        mgr.PushFreePage(5);
        Assert.Equal(3, mgr.FreelistPageCount);
    }

    [Fact]
    public void PushAndPop_AcrossMultipleTrunks_Correct()
    {
        // Max leaves per trunk: (4096 - 8) / 4 = 1022
        int maxLeaves = (PageSize - 8) / 4;
        var source = CreateSource(maxLeaves + 10);
        var shadow = new ShadowPageSource(source);
        var mgr = new FreelistManager(shadow, PageSize);
        mgr.Initialize(0, 0);

        // Push enough pages to fill one trunk and create a second
        // First push becomes trunk (page 3), next maxLeaves pushes become leaves
        // Then one more push creates a second trunk
        for (uint i = 3; i <= (uint)(3 + maxLeaves + 1); i++)
            mgr.PushFreePage(i);

        int totalPushed = maxLeaves + 2; // trunk + maxLeaves leaves + new trunk
        Assert.Equal(totalPushed, mgr.FreelistPageCount);

        // Pop all pages
        var recovered = new HashSet<uint>();
        while (mgr.HasFreePages)
        {
            uint page = mgr.PopFreePage();
            Assert.NotEqual(0u, page);
            recovered.Add(page);
        }

        Assert.Equal(totalPushed, recovered.Count);
    }
}