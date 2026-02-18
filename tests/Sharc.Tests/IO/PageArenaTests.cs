// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Core.IO;
using Xunit;

namespace Sharc.Tests.IO;

/// <summary>
/// Tests for <see cref="PageArena"/> — a contiguous page buffer arena
/// that sub-allocates page-sized slots via bump pointer (ADR-016 Tier 4).
/// </summary>
public sealed class PageArenaTests
{
    private const int PageSize = 4096;

    [Fact]
    public void Allocate_ReturnsCorrectSizedSpan()
    {
        using var arena = new PageArena(PageSize, initialCapacity: 4);
        var span = arena.Allocate(out int slot);

        Assert.Equal(0, slot);
        Assert.Equal(PageSize, span.Length);
    }

    [Fact]
    public void Allocate_MultipleSlots_NonOverlapping()
    {
        using var arena = new PageArena(PageSize, initialCapacity: 4);

        var span0 = arena.Allocate(out int slot0);
        span0[0] = 0xAA;
        span0[PageSize - 1] = 0xBB;

        var span1 = arena.Allocate(out int slot1);
        span1[0] = 0xCC;
        span1[PageSize - 1] = 0xDD;

        Assert.Equal(0, slot0);
        Assert.Equal(1, slot1);

        // Verify data didn't overlap — re-read slot 0
        var reread0 = arena.GetSlot(slot0);
        Assert.Equal(0xAA, reread0[0]);
        Assert.Equal(0xBB, reread0[PageSize - 1]);

        var reread1 = arena.GetSlot(slot1);
        Assert.Equal(0xCC, reread1[0]);
        Assert.Equal(0xDD, reread1[PageSize - 1]);
    }

    [Fact]
    public void GetSlot_ReturnsAllocatedData()
    {
        using var arena = new PageArena(PageSize, initialCapacity: 4);
        var span = arena.Allocate(out int slot);
        for (int i = 0; i < PageSize; i++)
            span[i] = (byte)(i & 0xFF);

        var retrieved = arena.GetSlot(slot);
        Assert.Equal(0x00, retrieved[0]);
        Assert.Equal(0xFF, retrieved[255]);
        Assert.Equal(0x00, retrieved[256]);
    }

    [Fact]
    public void Reset_AllowsReuse_DataOverwritten()
    {
        using var arena = new PageArena(PageSize, initialCapacity: 4);

        // First cycle
        var span = arena.Allocate(out int slot0);
        span[0] = 0xAA;
        Assert.Equal(0, slot0);

        arena.Reset();

        // Second cycle — should reuse slot 0
        var span2 = arena.Allocate(out int slot1);
        Assert.Equal(0, slot1);
        span2[0] = 0xBB;

        // Data from second cycle should be present
        var reread = arena.GetSlot(0);
        Assert.Equal(0xBB, reread[0]);
    }

    [Fact]
    public void Grow_WhenFull_PreservesExistingData()
    {
        using var arena = new PageArena(PageSize, initialCapacity: 2);

        // Fill to capacity
        var s0 = arena.Allocate(out _);
        s0[0] = 0x11;
        s0[PageSize - 1] = 0x22;

        var s1 = arena.Allocate(out _);
        s1[0] = 0x33;

        // Third allocation triggers growth
        var s2 = arena.Allocate(out int slot2);
        s2[0] = 0x55;
        Assert.Equal(2, slot2);

        // Verify original data survived growth
        var r0 = arena.GetSlot(0);
        Assert.Equal(0x11, r0[0]);
        Assert.Equal(0x22, r0[PageSize - 1]);

        var r1 = arena.GetSlot(1);
        Assert.Equal(0x33, r1[0]);
    }

    [Fact]
    public void SlotCount_TracksAllocations()
    {
        using var arena = new PageArena(PageSize, initialCapacity: 8);
        Assert.Equal(0, arena.SlotCount);

        arena.Allocate(out _);
        Assert.Equal(1, arena.SlotCount);

        arena.Allocate(out _);
        arena.Allocate(out _);
        Assert.Equal(3, arena.SlotCount);

        arena.Reset();
        Assert.Equal(0, arena.SlotCount);
    }

    [Fact]
    public void MultipleResetCycles_NoErrors()
    {
        using var arena = new PageArena(PageSize, initialCapacity: 4);

        for (int cycle = 0; cycle < 20; cycle++)
        {
            for (int i = 0; i < 3; i++)
            {
                var span = arena.Allocate(out _);
                span[0] = (byte)cycle;
            }
            arena.Reset();
        }

        Assert.Equal(0, arena.SlotCount);
    }
}
