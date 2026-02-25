// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Core.Format;
using Sharc.Exceptions;
using Xunit;

namespace Sharc.Tests.Fuzz;

/// <summary>
/// TD-9: Fuzz testing for DatabaseHeader and BTreePageHeader parsers.
/// Ensures header parsing handles adversarial inputs gracefully.
/// </summary>
public sealed class DatabaseHeaderFuzzTests
{
    private static readonly Random Rng = new(42);

    [Fact]
    public void Parse_RandomBytes_NeverCrashes()
    {
        var buffer = new byte[100];
        for (int trial = 0; trial < 200; trial++)
        {
            Rng.NextBytes(buffer);
            try
            {
                DatabaseHeader.Parse(buffer);
            }
            catch (InvalidDatabaseException)
            {
                // Expected — random bytes won't have valid magic
            }
        }
    }

    [Fact]
    public void Parse_TooShort_Throws()
    {
        var buffer = new byte[50]; // less than 100
        Assert.Throws<InvalidDatabaseException>(() => DatabaseHeader.Parse(buffer));
    }

    [Fact]
    public void Parse_ValidMagicRandomRest_DoesNotCrash()
    {
        var buffer = new byte[100];
        Rng.NextBytes(buffer);
        // Write valid magic
        "SQLite format 3\0"u8.CopyTo(buffer);
        // Should parse without crashing (may have odd field values)
        var header = DatabaseHeader.Parse(buffer);
        Assert.True(header.PageSize >= 0);
    }

    [Fact]
    public void HasValidMagic_RandomBytes_NeverCrashes()
    {
        var buffer = new byte[16];
        for (int trial = 0; trial < 100; trial++)
        {
            Rng.NextBytes(buffer);
            bool result = DatabaseHeader.HasValidMagic(buffer);
            Assert.False(result); // random bytes won't match magic
        }
    }

    [Fact]
    public void HasValidMagic_TooShort_ReturnsFalse()
    {
        Assert.False(DatabaseHeader.HasValidMagic(ReadOnlySpan<byte>.Empty));
        Assert.False(DatabaseHeader.HasValidMagic(new byte[5]));
    }

    // ── BTreePageHeader fuzz ────────────────────────────────────────

    [Fact]
    public void BTreePageHeader_Parse_RandomBytes_NeverCrashes()
    {
        var buffer = new byte[12]; // interior header is 12 bytes
        for (int trial = 0; trial < 200; trial++)
        {
            Rng.NextBytes(buffer);
            try
            {
                BTreePageHeader.Parse(buffer);
            }
            catch (CorruptPageException)
            {
                // Expected for invalid page type flags
            }
        }
    }

    [Fact]
    public void BTreePageHeader_Parse_ValidTypeFlags_DoNotCrash()
    {
        byte[] validTypes = [0x02, 0x05, 0x0A, 0x0D];
        var buffer = new byte[12];
        foreach (byte typeFlag in validTypes)
        {
            Rng.NextBytes(buffer);
            buffer[0] = typeFlag;
            var header = BTreePageHeader.Parse(buffer);
            Assert.Equal((BTreePageType)typeFlag, header.PageType);
        }
    }

    [Fact]
    public void BTreePageHeader_Parse_AllZeros_ThrowsCorruptPage()
    {
        var buffer = new byte[12];
        Assert.Throws<CorruptPageException>(() => BTreePageHeader.Parse(buffer));
    }
}
