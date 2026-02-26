// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Core.Primitives;
using Xunit;

namespace Sharc.Tests;

public sealed class DecimalCodecTests
{
    [Theory]
    [InlineData("0")]
    [InlineData("1")]
    [InlineData("-1")]
    [InlineData("123456789.987654321")]
    [InlineData("79228162514264337593543950335")] // decimal.MaxValue
    [InlineData("-79228162514264337593543950335")] // decimal.MinValue
    public void EncodeDecode_RoundTrips(string valueText)
    {
        decimal value = decimal.Parse(valueText, System.Globalization.CultureInfo.InvariantCulture);
        byte[] encoded = DecimalCodec.Encode(value);
        decimal decoded = DecimalCodec.Decode(encoded);
        Assert.Equal(value, decoded);
    }

    [Fact]
    public void Encode_NormalizesTrailingZeros()
    {
        byte[] a = DecimalCodec.Encode(1.2300m);
        byte[] b = DecimalCodec.Encode(1.23m);
        Assert.True(a.AsSpan().SequenceEqual(b));
    }

    [Fact]
    public void TryDecode_InvalidLength_ReturnsFalse()
    {
        Assert.False(DecimalCodec.TryDecode([1, 2, 3], out _));
    }
}
