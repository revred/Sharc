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
/// Tests for ShadowPageSource.PageCount — validates O(1) max-tracking
/// replaces the previous LINQ Max() approach.
/// </summary>
public sealed class ShadowPageSourcePageCountTests
{
    private const int PageSize = 4096;

    private static MemoryPageSource CreateBase(int pages)
    {
        var data = new byte[PageSize * pages];
        "SQLite format 3\0"u8.CopyTo(data);
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(16), (ushort)PageSize);
        data[18] = 1;
        data[19] = 1;
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(28), (uint)pages);
        return new MemoryPageSource(data);
    }

    [Fact]
    public void PageCount_NoDirtyPages_ReturnsBaseCount()
    {
        using var baseSource = CreateBase(3);
        using var shadow = new ShadowPageSource(baseSource);

        Assert.Equal(3, shadow.PageCount);
    }

    [Fact]
    public void PageCount_DirtyPageWithinBaseRange_ReturnsBaseCount()
    {
        using var baseSource = CreateBase(3);
        using var shadow = new ShadowPageSource(baseSource);

        shadow.WritePage(2, new byte[PageSize]);

        // Dirty page 2 is within base range (3 pages), so PageCount stays 3
        Assert.Equal(3, shadow.PageCount);
    }

    [Fact]
    public void PageCount_DirtyPageBeyondBaseRange_ReturnsMaxDirty()
    {
        using var baseSource = CreateBase(2);
        using var shadow = new ShadowPageSource(baseSource);

        shadow.WritePage(5, new byte[PageSize]);

        Assert.Equal(5, shadow.PageCount);
    }

    [Fact]
    public void PageCount_MultipleDirtyPages_ReturnsCorrectMax()
    {
        using var baseSource = CreateBase(2);
        using var shadow = new ShadowPageSource(baseSource);

        shadow.WritePage(3, new byte[PageSize]);
        shadow.WritePage(7, new byte[PageSize]);
        shadow.WritePage(5, new byte[PageSize]);

        Assert.Equal(7, shadow.PageCount);
    }

    [Fact]
    public void PageCount_AfterClearShadow_ReturnsBaseCount()
    {
        using var baseSource = CreateBase(2);
        using var shadow = new ShadowPageSource(baseSource);

        shadow.WritePage(10, new byte[PageSize]);
        Assert.Equal(10, shadow.PageCount);

        shadow.ClearShadow();

        Assert.Equal(2, shadow.PageCount);
    }

    [Fact]
    public void PageCount_CalledRepeatedly_Consistent()
    {
        using var baseSource = CreateBase(2);
        using var shadow = new ShadowPageSource(baseSource);

        shadow.WritePage(4, new byte[PageSize]);

        // Multiple calls should return the same value (no LINQ enumerator state issues)
        Assert.Equal(4, shadow.PageCount);
        Assert.Equal(4, shadow.PageCount);
        Assert.Equal(4, shadow.PageCount);
    }
}