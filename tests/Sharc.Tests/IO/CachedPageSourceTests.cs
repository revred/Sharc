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
using Xunit;

namespace Sharc.Tests.IO;

public class CachedPageSourceTests
{
    private static byte[] CreateMinimalDatabase(int pageSize = 4096, int pageCount = 5)
    {
        var data = new byte[pageSize * pageCount];
        "SQLite format 3\0"u8.CopyTo(data);
        data[16] = (byte)(pageSize >> 8);
        data[17] = (byte)(pageSize & 0xFF);
        data[18] = 1; data[19] = 1;
        data[20] = 0; data[21] = 64; data[22] = 32; data[23] = 32;
        data[28] = (byte)(pageCount >> 24);
        data[29] = (byte)(pageCount >> 16);
        data[30] = (byte)(pageCount >> 8);
        data[31] = (byte)(pageCount & 0xFF);
        data[47] = 4;
        data[56] = 0; data[57] = 0; data[58] = 0; data[59] = 1;

        // Write known marker at start of each page
        for (int p = 0; p < pageCount; p++)
            data[p * pageSize + (p == 0 ? 100 : 0)] = (byte)(0xA0 + p);

        return data;
    }

    [Fact]
    public void Constructor_WrapsInnerSource_ExposesPageSizeAndCount()
    {
        var data = CreateMinimalDatabase(pageSize: 4096, pageCount: 5);
        using var inner = new MemoryPageSource(data);
        using var cached = new CachedPageSource(inner, capacity: 10);

        Assert.Equal(4096, cached.PageSize);
        Assert.Equal(5, cached.PageCount);
    }

    [Fact]
    public void GetPage_FirstAccess_ReturnsCorrectData()
    {
        var data = CreateMinimalDatabase();
        using var inner = new MemoryPageSource(data);
        using var cached = new CachedPageSource(inner, capacity: 10);

        var page2 = cached.GetPage(2);

        Assert.Equal(0xA1, page2[0]); // marker for page 2
        Assert.Equal(1, cached.CacheMissCount);
        Assert.Equal(0, cached.CacheHitCount);
    }

    [Fact]
    public void GetPage_SecondAccess_ReturnsCachedData()
    {
        var data = CreateMinimalDatabase();
        using var inner = new MemoryPageSource(data);
        using var cached = new CachedPageSource(inner, capacity: 10);

        cached.GetPage(2); // miss
        var page2Again = cached.GetPage(2); // hit

        Assert.Equal(0xA1, page2Again[0]);
        Assert.Equal(1, cached.CacheMissCount);
        Assert.Equal(1, cached.CacheHitCount);
    }

    [Fact]
    public void GetPage_ExceedsCapacity_EvictsLeastRecentlyUsed()
    {
        var data = CreateMinimalDatabase(pageCount: 5);
        using var inner = new MemoryPageSource(data);
        using var cached = new CachedPageSource(inner, capacity: 2);

        cached.GetPage(1); // miss â€” cache: [1]
        cached.GetPage(2); // miss â€” cache: [2, 1]
        cached.GetPage(3); // miss, evicts 1 â€” cache: [3, 2]

        Assert.Equal(3, cached.CacheMissCount);

        // Page 2 should still be cached
        cached.GetPage(2); // hit
        Assert.Equal(1, cached.CacheHitCount);

        // Page 1 was evicted â€” re-accessing is a miss
        cached.GetPage(1); // miss
        Assert.Equal(4, cached.CacheMissCount);
    }

    [Fact]
    public void GetPage_RecentlyAccessed_IsNotEvicted()
    {
        var data = CreateMinimalDatabase(pageCount: 5);
        using var inner = new MemoryPageSource(data);
        using var cached = new CachedPageSource(inner, capacity: 2);

        cached.GetPage(1); // miss â€” cache: [1]
        cached.GetPage(2); // miss â€” cache: [2, 1]
        cached.GetPage(1); // hit, moves to front â€” cache: [1, 2]
        cached.GetPage(3); // miss, evicts 2 â€” cache: [3, 1]

        // Page 1 should still be cached (was moved to front)
        cached.GetPage(1); // hit
        Assert.Equal(2, cached.CacheHitCount); // one from the touch, one from this access

        // Page 2 was evicted
        cached.GetPage(2); // miss
        Assert.Equal(4, cached.CacheMissCount);
    }

    [Fact]
    public void ReadPage_CopiesDataToDestination()
    {
        var data = CreateMinimalDatabase();
        using var inner = new MemoryPageSource(data);
        using var cached = new CachedPageSource(inner, capacity: 10);

        var buffer = new byte[4096];
        var bytesRead = cached.ReadPage(2, buffer);

        Assert.Equal(4096, bytesRead);
        Assert.Equal(0xA1, buffer[0]);
    }

    [Fact]
    public void ReadPage_PopulatesCache_SubsequentGetPageIsHit()
    {
        var data = CreateMinimalDatabase();
        using var inner = new MemoryPageSource(data);
        using var cached = new CachedPageSource(inner, capacity: 10);

        var buffer = new byte[4096];
        cached.ReadPage(2, buffer); // miss
        cached.GetPage(2); // hit

        Assert.Equal(1, cached.CacheMissCount);
        Assert.Equal(1, cached.CacheHitCount);
    }

    [Fact]
    public void GetPage_ZeroCapacity_AlwaysDelegatesToInner()
    {
        var data = CreateMinimalDatabase();
        using var inner = new MemoryPageSource(data);
        using var cached = new CachedPageSource(inner, capacity: 0);

        var page = cached.GetPage(2);
        Assert.Equal(0xA1, page[0]);

        // No caching â€” counters stay at 0
        Assert.Equal(0, cached.CacheHitCount);
        Assert.Equal(0, cached.CacheMissCount);
    }

    [Fact]
    public void GetPage_PageZero_ThrowsArgumentOutOfRange()
    {
        var data = CreateMinimalDatabase();
        using var inner = new MemoryPageSource(data);
        using var cached = new CachedPageSource(inner, capacity: 10);

        Assert.Throws<ArgumentOutOfRangeException>(() => { _ = cached.GetPage(0); });
    }

    [Fact]
    public void GetPage_PageBeyondCount_ThrowsArgumentOutOfRange()
    {
        var data = CreateMinimalDatabase(pageCount: 3);
        using var inner = new MemoryPageSource(data);
        using var cached = new CachedPageSource(inner, capacity: 10);

        Assert.Throws<ArgumentOutOfRangeException>(() => { _ = cached.GetPage(4); });
    }

    [Fact]
    public void Dispose_IsIdempotent()
    {
        var data = CreateMinimalDatabase();
        using var inner = new MemoryPageSource(data);
        var cached = new CachedPageSource(inner, capacity: 10);

        cached.GetPage(1);
        cached.Dispose();
        cached.Dispose(); // should not throw
    }

    [Fact]
    public void Dispose_SubsequentGetPage_ThrowsObjectDisposed()
    {
        var data = CreateMinimalDatabase();
        using var inner = new MemoryPageSource(data);
        var cached = new CachedPageSource(inner, capacity: 10);

        cached.Dispose();

        Assert.Throws<ObjectDisposedException>(() => { _ = cached.GetPage(1); });
    }

    [Fact]
    public void GetPage_AllPages_CorrectData()
    {
        var data = CreateMinimalDatabase(pageCount: 5);
        using var inner = new MemoryPageSource(data);
        using var cached = new CachedPageSource(inner, capacity: 10);

        for (uint p = 1; p <= 5; p++)
        {
            var page = cached.GetPage(p);
            var expectedOffset = p == 1 ? 100 : 0;
            Assert.Equal((byte)(0xA0 + p - 1), page[expectedOffset]);
        }

        Assert.Equal(5, cached.CacheMissCount);
    }

    // --- Demand-Driven Allocation Tests ---

    [Fact]
    public void Constructor_DemandDriven_NoBuffersRentedAtConstruction()
    {
        var data = CreateMinimalDatabase();
        using var inner = new MemoryPageSource(data);
        using var cached = new CachedPageSource(inner, capacity: 2000);

        // Capacity is a maximum, not a reservation.
        // No page buffers should be rented until first access.
        Assert.Equal(0, cached.AllocatedSlotCount);
    }

    [Fact]
    public void GetPage_FirstAccess_RentsOneBuffer()
    {
        var data = CreateMinimalDatabase();
        using var inner = new MemoryPageSource(data);
        using var cached = new CachedPageSource(inner, capacity: 100);

        cached.GetPage(1);

        Assert.Equal(1, cached.AllocatedSlotCount);
    }

    [Fact]
    public void GetPage_NDistinctPages_RentsNBuffers()
    {
        var data = CreateMinimalDatabase(pageCount: 5);
        using var inner = new MemoryPageSource(data);
        using var cached = new CachedPageSource(inner, capacity: 100);

        cached.GetPage(1);
        cached.GetPage(2);
        cached.GetPage(3);

        Assert.Equal(3, cached.AllocatedSlotCount);
    }

    [Fact]
    public void GetPage_CacheHit_DoesNotRentAdditionalBuffer()
    {
        var data = CreateMinimalDatabase(pageCount: 5);
        using var inner = new MemoryPageSource(data);
        using var cached = new CachedPageSource(inner, capacity: 100);

        cached.GetPage(1);
        cached.GetPage(2);
        cached.GetPage(1); // hit — no new buffer

        Assert.Equal(2, cached.AllocatedSlotCount);
    }

    [Fact]
    public void Dispose_WithNoAccesses_ReturnsNoBuffers()
    {
        var data = CreateMinimalDatabase();
        using var inner = new MemoryPageSource(data);
        var cached = new CachedPageSource(inner, capacity: 2000);

        Assert.Equal(0, cached.AllocatedSlotCount);
        cached.Dispose(); // should not throw — nothing to return
    }

    [Fact]
    public void Eviction_ReusesExistingBuffer_NoNewRent()
    {
        var data = CreateMinimalDatabase(pageCount: 5);
        using var inner = new MemoryPageSource(data);
        using var cached = new CachedPageSource(inner, capacity: 2);

        cached.GetPage(1); // rent slot 0
        cached.GetPage(2); // rent slot 1 — cache full (2/2)
        Assert.Equal(2, cached.AllocatedSlotCount);

        cached.GetPage(3); // evicts page 1, reuses slot — no new rent
        Assert.Equal(2, cached.AllocatedSlotCount);

        cached.GetPage(4); // evicts page 2, reuses slot — no new rent
        Assert.Equal(2, cached.AllocatedSlotCount);
    }
}