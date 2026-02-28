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

namespace Sharc.Tests.Write;

/// <summary>
/// Tests for ShadowPageSource ArrayPool-backed dirty page buffers.
/// Validates that dirty page data round-trips correctly with pooled buffers
/// and that disposal returns them properly.
/// </summary>
public sealed class ShadowPageSourcePoolingTests
{
    private const int PageSize = 4096;

    private static MemoryPageSource CreateBase(int pages = 2)
    {
        var data = new byte[PageSize * pages];
        // Minimal SQLite header so MemoryPageSource accepts it
        "SQLite format 3\0"u8.CopyTo(data);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(16), (ushort)PageSize);
        data[18] = 1;
        data[19] = 1;
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(28), (uint)pages);
        return new MemoryPageSource(data);
    }

    [Fact]
    public void WritePage_ThenReadPage_DataRoundTrips()
    {
        using var baseSource = CreateBase();
        using var shadow = new ShadowPageSource(baseSource);

        var writeData = new byte[PageSize];
        writeData[0] = 0xAB;
        writeData[100] = 0xCD;
        writeData[PageSize - 1] = 0xEF;

        shadow.WritePage(2, writeData);

        var readBuf = new byte[PageSize];
        shadow.ReadPage(2, readBuf);

        Assert.Equal(0xAB, readBuf[0]);
        Assert.Equal(0xCD, readBuf[100]);
        Assert.Equal(0xEF, readBuf[PageSize - 1]);
    }

    [Fact]
    public void WritePage_SamePageTwice_LatestDataPresent()
    {
        using var baseSource = CreateBase();
        using var shadow = new ShadowPageSource(baseSource);

        var first = new byte[PageSize];
        first[0] = 0x11;
        shadow.WritePage(2, first);

        var second = new byte[PageSize];
        second[0] = 0x22;
        shadow.WritePage(2, second);

        var readBuf = new byte[PageSize];
        shadow.ReadPage(2, readBuf);

        Assert.Equal(0x22, readBuf[0]);
    }

    [Fact]
    public void ClearShadow_ReturnsBuffers_DirtyPagesEmpty()
    {
        using var baseSource = CreateBase();
        using var shadow = new ShadowPageSource(baseSource);

        var data = new byte[PageSize];
        data[0] = 0xFF;
        shadow.WritePage(2, data);

        Assert.Equal(1, shadow.DirtyPageCount);

        shadow.ClearShadow();

        Assert.Equal(0, shadow.DirtyPageCount);
    }

    [Fact]
    public void Dispose_ReturnsBuffers_Idempotent()
    {
        var baseSource = CreateBase();
        var shadow = new ShadowPageSource(baseSource);

        var data = new byte[PageSize];
        shadow.WritePage(2, data);

        shadow.Dispose();

        // Second dispose should not throw
        shadow.Dispose();
    }

    [Fact]
    public void Dispose_CalledTwice_NoException()
    {
        var baseSource = CreateBase();
        var shadow = new ShadowPageSource(baseSource);
        shadow.Dispose();
        shadow.Dispose(); // Should not throw
    }

    [Fact]
    public void ReadPage_AfterClearShadow_FallsBackToBase()
    {
        using var baseSource = CreateBase();
        using var shadow = new ShadowPageSource(baseSource);

        var dirtyData = new byte[PageSize];
        dirtyData[0] = 0xAA;
        shadow.WritePage(1, dirtyData);

        shadow.ClearShadow();

        // Should fall back to base source data (SQLite header on page 1)
        var readBuf = new byte[PageSize];
        shadow.ReadPage(1, readBuf);

        // First bytes of base page 1 are "SQLite format 3\0"
        Assert.Equal((byte)'S', readBuf[0]);
        Assert.Equal((byte)'Q', readBuf[1]);
    }

    [Fact]
    public void Reset_ReturnBuffersAndClearPages_ObjectReusable()
    {
        using var baseSource = CreateBase();
        using var shadow = new ShadowPageSource(baseSource);

        var data = new byte[PageSize];
        data[0] = 0xAA;
        shadow.WritePage(2, data);
        Assert.Equal(1, shadow.DirtyPageCount);

        // Reset should clear dirty pages but keep the object usable
        shadow.Reset();
        Assert.Equal(0, shadow.DirtyPageCount);

        // Should be usable again after Reset
        data[0] = 0xBB;
        shadow.WritePage(2, data);
        Assert.Equal(1, shadow.DirtyPageCount);

        var readBuf = new byte[PageSize];
        shadow.ReadPage(2, readBuf);
        Assert.Equal(0xBB, readBuf[0]);
    }

    [Fact]
    public void Reset_MultipleCycles_NoMemoryGrowth()
    {
        using var baseSource = CreateBase(4);
        using var shadow = new ShadowPageSource(baseSource);

        for (int cycle = 0; cycle < 10; cycle++)
        {
            var data = new byte[PageSize];
            data[0] = (byte)cycle;
            shadow.WritePage(2, data);
            shadow.WritePage(3, data);
            shadow.Reset();
        }

        // After final reset, no dirty pages
        Assert.Equal(0, shadow.DirtyPageCount);
    }

    [Fact]
    public void WritePage_BufferSlicedToPageSize_ExtraBytesIgnored()
    {
        using var baseSource = CreateBase();
        using var shadow = new ShadowPageSource(baseSource);

        // Write exactly PageSize bytes
        var data = new byte[PageSize];
        for (int i = 0; i < PageSize; i++)
            data[i] = (byte)(i & 0xFF);
        shadow.WritePage(2, data);

        // GetPage should return exactly PageSize bytes
        var page = shadow.GetPage(2);
        Assert.Equal(PageSize, page.Length);
        Assert.Equal(0, page[0]);
        Assert.Equal(0xFF, page[0xFF]);
    }
}