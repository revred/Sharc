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

using Sharc.Core.IO;
using System.Buffers.Binary;
using Xunit;

namespace Sharc.Tests.Write;

/// <summary>
/// Tests for FreelistManager's ability to read freelist trunk pages
/// and pop free pages for reuse by BTreeMutator.
/// </summary>
public sealed class FreelistManagerTests
{
    private const int PageSize = 4096;

    /// <summary>
    /// Creates a MemoryPageSource with a valid header and optional freelist trunk pages.
    /// </summary>
    private static MemoryPageSource CreateSource(int totalPages, uint firstTrunk = 0, int freelistCount = 0)
    {
        var data = new byte[PageSize * totalPages];

        // SQLite header on page 1
        "SQLite format 3\0"u8.CopyTo(data);
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(16), PageSize);
        data[18] = 1;
        data[19] = 1;
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(28), (uint)totalPages);
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(32), firstTrunk);
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(36), (uint)freelistCount);

        return new MemoryPageSource(data);
    }

    /// <summary>
    /// Writes a freelist trunk page at the given page number.
    /// </summary>
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

    [Fact]
    public void PopFreePage_EmptyFreelist_ReturnsZero()
    {
        var source = CreateSource(3);
        var shadow = new ShadowPageSource(source);
        var mgr = new FreelistManager(shadow, PageSize);
        mgr.Initialize(0, 0);

        uint page = mgr.PopFreePage();

        Assert.Equal(0u, page);
    }

    [Fact]
    public void PopFreePage_SingleTrunkSingleLeaf_ReturnsLeafPage()
    {
        // Trunk on page 3 with one leaf (page 4)
        var source = CreateSource(5, firstTrunk: 3, freelistCount: 2);
        WriteTrunkPage(source, 3, nextTrunk: 0, leafPages: [4]);

        var shadow = new ShadowPageSource(source);
        var mgr = new FreelistManager(shadow, PageSize);
        mgr.Initialize(3, 2);

        uint page = mgr.PopFreePage();

        Assert.Equal(4u, page);
        Assert.Equal(1, mgr.FreelistPageCount);
    }

    [Fact]
    public void PopFreePage_SingleTrunkMultipleLeaves_ReturnsLastLeaf()
    {
        // Trunk on page 3 with three leaves: 5, 6, 7
        var source = CreateSource(8, firstTrunk: 3, freelistCount: 4);
        WriteTrunkPage(source, 3, nextTrunk: 0, leafPages: [5, 6, 7]);

        var shadow = new ShadowPageSource(source);
        var mgr = new FreelistManager(shadow, PageSize);
        mgr.Initialize(3, 4);

        uint first = mgr.PopFreePage();
        Assert.Equal(7u, first);
        Assert.Equal(3, mgr.FreelistPageCount);

        uint second = mgr.PopFreePage();
        Assert.Equal(6u, second);
        Assert.Equal(2, mgr.FreelistPageCount);
    }

    [Fact]
    public void PopFreePage_MultipleTrunks_ExhaustFirstTrunkThenAdvances()
    {
        // Trunk on page 3 → next trunk on page 4
        // Page 3: leaves [5]
        // Page 4: leaves [6, 7]
        var source = CreateSource(8, firstTrunk: 3, freelistCount: 5);
        WriteTrunkPage(source, 3, nextTrunk: 4, leafPages: [5]);
        WriteTrunkPage(source, 4, nextTrunk: 0, leafPages: [6, 7]);

        var shadow = new ShadowPageSource(source);
        var mgr = new FreelistManager(shadow, PageSize);
        mgr.Initialize(3, 5);

        // Pop leaf from trunk 3
        Assert.Equal(5u, mgr.PopFreePage());
        Assert.Equal(4, mgr.FreelistPageCount);

        // Trunk 3 has no more leaves, pop trunk 3 itself
        Assert.Equal(3u, mgr.PopFreePage());
        Assert.Equal(3, mgr.FreelistPageCount);

        // Now on trunk 4, pop its leaves
        Assert.Equal(7u, mgr.PopFreePage());
        Assert.Equal(2, mgr.FreelistPageCount);

        Assert.Equal(6u, mgr.PopFreePage());
        Assert.Equal(1, mgr.FreelistPageCount);
    }

    [Fact]
    public void PopFreePage_TrunkOnlyNoLeaves_ReturnsTrunkItself()
    {
        // Trunk on page 3 with no leaves
        var source = CreateSource(4, firstTrunk: 3, freelistCount: 1);
        WriteTrunkPage(source, 3, nextTrunk: 0, leafPages: []);

        var shadow = new ShadowPageSource(source);
        var mgr = new FreelistManager(shadow, PageSize);
        mgr.Initialize(3, 1);

        uint page = mgr.PopFreePage();

        Assert.Equal(3u, page);
        Assert.Equal(0, mgr.FreelistPageCount);
        Assert.False(mgr.HasFreePages);
    }

    [Fact]
    public void HasFreePages_EmptyFreelist_ReturnsFalse()
    {
        var source = CreateSource(3);
        var shadow = new ShadowPageSource(source);
        var mgr = new FreelistManager(shadow, PageSize);
        mgr.Initialize(0, 0);

        Assert.False(mgr.HasFreePages);
    }

    [Fact]
    public void HasFreePages_NonEmptyFreelist_ReturnsTrue()
    {
        var source = CreateSource(5, firstTrunk: 3, freelistCount: 2);
        WriteTrunkPage(source, 3, nextTrunk: 0, leafPages: [4]);

        var shadow = new ShadowPageSource(source);
        var mgr = new FreelistManager(shadow, PageSize);
        mgr.Initialize(3, 2);

        Assert.True(mgr.HasFreePages);
    }

    [Fact]
    public void PopFreePage_AllPagesPopped_FreelistEmpty()
    {
        // Trunk on page 3, leaf page 4 → total 2 freelist pages
        var source = CreateSource(5, firstTrunk: 3, freelistCount: 2);
        WriteTrunkPage(source, 3, nextTrunk: 0, leafPages: [4]);

        var shadow = new ShadowPageSource(source);
        var mgr = new FreelistManager(shadow, PageSize);
        mgr.Initialize(3, 2);

        mgr.PopFreePage(); // pops 4
        mgr.PopFreePage(); // pops 3 (trunk itself)

        Assert.Equal(0, mgr.FreelistPageCount);
        Assert.False(mgr.HasFreePages);
        Assert.Equal(0u, mgr.FirstTrunkPage);
        Assert.Equal(0u, mgr.PopFreePage()); // no more pages
    }
}