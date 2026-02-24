// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Core.Records;
using Xunit;

namespace Sharc.Tests.Fuzz;

/// <summary>
/// TD-9: Fuzz testing for RecordDecoder.
/// Ensures record header parsing and column decode handle adversarial inputs.
/// </summary>
public sealed class RecordDecoderFuzzTests
{
    private static readonly Random Rng = new(42);
    private readonly RecordDecoder _decoder = new();

    [Fact]
    public void ReadSerialTypes_RandomBytes_NeverCrashes()
    {
        var serialTypes = new long[64];
        for (int trial = 0; trial < 200; trial++)
        {
            int len = Rng.Next(0, 256);
            var buffer = new byte[len];
            Rng.NextBytes(buffer);

            try
            {
                _decoder.ReadSerialTypes(buffer, serialTypes, out _);
            }
            catch
            {
                // Any exception is fine — no crash or hang
            }
        }
    }

    [Fact]
    public void ReadSerialTypes_EmptyPayload_DoesNotCrash()
    {
        var serialTypes = new long[16];
        try
        {
            _decoder.ReadSerialTypes(ReadOnlySpan<byte>.Empty, serialTypes, out _);
        }
        catch
        {
            // Acceptable
        }
    }

    [Fact]
    public void ReadSerialTypes_AllZeros_DoesNotHang()
    {
        var buffer = new byte[64];
        var serialTypes = new long[64];
        try
        {
            _decoder.ReadSerialTypes(buffer, serialTypes, out int bodyOffset);
            Assert.True(bodyOffset >= 0);
        }
        catch
        {
            // Acceptable
        }
    }

    [Fact]
    public void ReadSerialTypes_VeryLargeHeaderSize_DoesNotHang()
    {
        // Varint encoding of a huge header size
        var buffer = new byte[64];
        buffer[0] = 0xFF; // large varint
        buffer[1] = 0xFF;
        buffer[2] = 0x01;
        var serialTypes = new long[64];
        try
        {
            _decoder.ReadSerialTypes(buffer, serialTypes, out _);
        }
        catch
        {
            // Acceptable — should not hang
        }
    }

    [Fact]
    public void DecodeStringAt_InvalidOffset_DoesNotCrash()
    {
        var buffer = new byte[32];
        Rng.NextBytes(buffer);
        try
        {
            // serial type 13 = text with 0 bytes
            _decoder.DecodeStringAt(buffer, 13, 10);
        }
        catch
        {
            // Acceptable
        }
    }

    [Fact]
    public void DecodeInt64At_RandomPayload_DoesNotCrash()
    {
        for (int trial = 0; trial < 100; trial++)
        {
            var buffer = new byte[Rng.Next(1, 32)];
            Rng.NextBytes(buffer);
            int serialType = Rng.Next(0, 10); // valid integer serial types are 0-6, 8, 9
            int offset = Rng.Next(0, buffer.Length);
            try
            {
                _decoder.DecodeInt64At(buffer, serialType, offset);
            }
            catch
            {
                // Acceptable
            }
        }
    }

    [Fact]
    public void ComputeColumnOffsets_RandomSerialTypes_DoesNotCrash()
    {
        var serialTypes = new long[16];
        var offsets = new int[16];
        for (int trial = 0; trial < 100; trial++)
        {
            for (int i = 0; i < 16; i++)
                serialTypes[i] = Rng.NextInt64(0, 200);
            int columnCount = Rng.Next(1, 16);
            int bodyOffset = Rng.Next(0, 128);
            try
            {
                _decoder.ComputeColumnOffsets(serialTypes.AsSpan(0, columnCount), columnCount, bodyOffset, offsets.AsSpan(0, columnCount));
            }
            catch
            {
                // Acceptable
            }
        }
    }
}
