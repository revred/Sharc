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

  Licensed under the MIT License — free for personal and commercial use.                         |
--------------------------------------------------------------------------------------------------*/

using Sharc.Graph.Model;
using Xunit;

namespace Sharc.Graph.Tests.Unit.Model;

public class NodeKeyTests
{
    [Fact]
    public void Value_StoresInteger()
    {
        var key = new NodeKey(42);
        Assert.Equal(42L, key.Value);
    }

    [Fact]
    public void ToAscii_DecodesBarId()
    {
        // "CMUSQD" -> 73999423066436
        // C=67, M=77, U=85, S=83, Q=81, D=68
        // 00 00 43 4D 55 53 51 44
        //         C  M  U  S  Q  D
        long barId = 73999423066436;
        var key = new NodeKey(barId);
        
        Assert.Equal("CMUSQD", key.ToAscii());
    }

    [Fact]
    public void FromAscii_EncodesCorrectly()
    {
        var key = NodeKey.FromAscii("CMUSQD");
        Assert.Equal(73999423066436L, key.Value);
    }

    [Fact]
    public void RoundTrip_AsciiToIntegerAndBack()
    {
        string original = "ABC123";
        var key = NodeKey.FromAscii(original);
        Assert.Equal(original, key.ToAscii());
    }

    [Fact]
    public void ImplicitLong_Works()
    {
        NodeKey k = 123;
        Assert.Equal(123L, k.Value);
        
        long v = k;
        Assert.Equal(123L, v);
    }

    [Fact]
    public void Equality_SameValue_Equal()
    {
        var k1 = new NodeKey(12345);
        var k2 = new NodeKey(12345);
        Assert.Equal(k1, k2);
        Assert.True(k1 == k2);
    }

    [Fact]
    public void Equality_DifferentValue_NotEqual()
    {
        var k1 = new NodeKey(12345);
        var k2 = new NodeKey(67890);
        Assert.NotEqual(k1, k2);
        Assert.False(k1 == k2);
    }

    [Fact]
    public void ToAscii_ZeroValue_ReturnsZero()
    {
        var k = new NodeKey(0);
        Assert.Equal("0", k.ToAscii());
    }

    [Fact]
    public void FromAscii_TooLong_Throws()
    {
        Assert.Throws<ArgumentException>(() => NodeKey.FromAscii("123456789"));
    }

    [Fact]
    public void ToAscii_NonAsciiValue_ReturnsNumericString()
    {
        // Value that contains non-printable ASCII (e.g., 0x01)
        var key = new NodeKey(1); // 00 00 00 00 00 00 00 01
        Assert.Equal("1", key.ToAscii());

        // Value that would be garbage ASCII
        var key2 = new NodeKey(0x0102030405060708L);
        Assert.Equal(0x0102030405060708L.ToString(System.Globalization.CultureInfo.InvariantCulture), key2.ToAscii());
    }
}