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

using Sharc.Core.Codec;
using Xunit;

namespace Sharc.Tests.Codec;

public class CborCodecTests
{
    // ──────────────────────────────────────────────────────────────────
    //  Roundtrip Tests
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public void Encode_EmptyMap_RoundTrips()
    {
        var input = new Dictionary<string, object?>();
        byte[] cbor = CborEncoder.Encode(input);
        var result = CborDecoder.Decode(cbor);

        Assert.Empty(result);
    }

    [Fact]
    public void Encode_StringValues_RoundTrips()
    {
        var input = new Dictionary<string, object?>
        {
            ["name"] = "Alice",
            ["city"] = "Portland"
        };

        byte[] cbor = CborEncoder.Encode(input);
        var result = CborDecoder.Decode(cbor);

        Assert.Equal(2, result.Count);
        Assert.Equal("Alice", result["name"]);
        Assert.Equal("Portland", result["city"]);
    }

    [Fact]
    public void Encode_IntegerValues_RoundTrips()
    {
        var input = new Dictionary<string, object?>
        {
            ["age"] = 30L,
            ["big"] = 1_000_000L,
            ["zero"] = 0L,
            ["negative"] = -42L
        };

        byte[] cbor = CborEncoder.Encode(input);
        var result = CborDecoder.Decode(cbor);

        Assert.Equal(4, result.Count);
        Assert.Equal(30L, result["age"]);
        Assert.Equal(1_000_000L, result["big"]);
        Assert.Equal(0L, result["zero"]);
        Assert.Equal(-42L, result["negative"]);
    }

    [Fact]
    public void Encode_DoubleValues_RoundTrips()
    {
        var input = new Dictionary<string, object?>
        {
            ["score"] = 3.14,
            ["negativeFloat"] = -0.5
        };

        byte[] cbor = CborEncoder.Encode(input);
        var result = CborDecoder.Decode(cbor);

        Assert.Equal(2, result.Count);
        Assert.Equal(3.14, (double)result["score"]!, 10);
        Assert.Equal(-0.5, (double)result["negativeFloat"]!, 10);
    }

    [Fact]
    public void Encode_BoolAndNull_RoundTrips()
    {
        var input = new Dictionary<string, object?>
        {
            ["active"] = true,
            ["verified"] = false,
            ["deleted"] = null
        };

        byte[] cbor = CborEncoder.Encode(input);
        var result = CborDecoder.Decode(cbor);

        Assert.Equal(3, result.Count);
        Assert.Equal(true, result["active"]);
        Assert.Equal(false, result["verified"]);
        Assert.Null(result["deleted"]);
    }

    [Fact]
    public void Encode_NestedMap_RoundTrips()
    {
        var inner = new Dictionary<string, object?>
        {
            ["source"] = "file.cs",
            ["line"] = 42L
        };
        var input = new Dictionary<string, object?>
        {
            ["meta"] = inner
        };

        byte[] cbor = CborEncoder.Encode(input);
        var result = CborDecoder.Decode(cbor);

        Assert.Single(result);
        var nested = Assert.IsType<Dictionary<string, object?>>(result["meta"]);
        Assert.Equal("file.cs", nested["source"]);
        Assert.Equal(42L, nested["line"]);
    }

    [Fact]
    public void Encode_ByteArray_RoundTrips()
    {
        var hash = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF, 0x01, 0x02, 0x03, 0x04 };
        var input = new Dictionary<string, object?>
        {
            ["hash"] = hash
        };

        byte[] cbor = CborEncoder.Encode(input);
        var result = CborDecoder.Decode(cbor);

        Assert.Single(result);
        Assert.Equal(hash, (byte[])result["hash"]!);
    }

    [Fact]
    public void Encode_MixedTypes_RoundTrips()
    {
        var input = new Dictionary<string, object?>
        {
            ["name"] = "Bob",
            ["age"] = 25L,
            ["score"] = 98.6,
            ["active"] = true,
            ["notes"] = (string?)null,
            ["sig"] = new byte[] { 0xAA, 0xBB },
            ["meta"] = new Dictionary<string, object?> { ["key"] = "val" }
        };

        byte[] cbor = CborEncoder.Encode(input);
        var result = CborDecoder.Decode(cbor);

        Assert.Equal(7, result.Count);
        Assert.Equal("Bob", result["name"]);
        Assert.Equal(25L, result["age"]);
        Assert.Equal(98.6, (double)result["score"]!, 10);
        Assert.Equal(true, result["active"]);
        Assert.Null(result["notes"]);
        Assert.Equal(new byte[] { 0xAA, 0xBB }, (byte[])result["sig"]!);
        var nested = Assert.IsType<Dictionary<string, object?>>(result["meta"]);
        Assert.Equal("val", nested["key"]);
    }

    // ──────────────────────────────────────────────────────────────────
    //  Selective Field Extraction
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public void ReadField_SelectiveExtraction()
    {
        var input = new Dictionary<string, object?>
        {
            ["first"] = "ignored",
            ["tokens"] = 512L,
            ["last"] = "also ignored"
        };

        byte[] cbor = CborEncoder.Encode(input);

        // Extract single field without decoding entire map
        var tokens = CborDecoder.ReadField(cbor, "tokens");
        Assert.Equal(512L, tokens);
    }

    [Fact]
    public void ReadField_MissingKey_ReturnsNull()
    {
        var input = new Dictionary<string, object?>
        {
            ["name"] = "Alice"
        };

        byte[] cbor = CborEncoder.Encode(input);

        var result = CborDecoder.ReadField(cbor, "nonexistent");
        Assert.Null(result);
    }

    [Fact]
    public void ReadField_TypedExtraction_ReturnsCorrectType()
    {
        var input = new Dictionary<string, object?>
        {
            ["count"] = 99L,
            ["label"] = "test"
        };

        byte[] cbor = CborEncoder.Encode(input);

        Assert.Equal(99L, CborDecoder.ReadField<long>(cbor, "count"));
        Assert.Equal("test", CborDecoder.ReadField<string>(cbor, "label"));
    }

    // ──────────────────────────────────────────────────────────────────
    //  Size & Validation
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public void Encode_LargePayload_SmallerThanJson()
    {
        // Build a 50-field map with representative data
        var input = new Dictionary<string, object?>();
        for (int i = 0; i < 50; i++)
        {
            input[$"field_{i:D3}"] = (i % 5) switch
            {
                0 => (object?)$"value_{i}",
                1 => (object?)(long)i,
                2 => (object?)(i * 1.1),
                3 => (object?)(i % 2 == 0),
                _ => null
            };
        }

        byte[] cbor = CborEncoder.Encode(input);
        byte[] json = System.Text.Encoding.UTF8.GetBytes(
            System.Text.Json.JsonSerializer.Serialize(input));

        // CBOR should be at least 25% smaller than JSON
        Assert.True(cbor.Length < json.Length * 0.75,
            $"CBOR ({cbor.Length} bytes) should be < 75% of JSON ({json.Length} bytes)");
    }

    [Fact]
    public void Decode_InvalidCbor_Throws()
    {
        // Truncated: map header says 5 entries but only has 0 bytes of content
        byte[] truncated = new byte[] { 0xA5 }; // map(5) with no entries

        Assert.ThrowsAny<Exception>(() => CborDecoder.Decode(truncated));
    }

    [Fact]
    public void Decode_EmptyInput_Throws()
    {
        Assert.ThrowsAny<Exception>(() => CborDecoder.Decode(ReadOnlySpan<byte>.Empty));
    }

    // ──────────────────────────────────────────────────────────────────
    //  Edge Cases
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public void Encode_EmptyString_RoundTrips()
    {
        var input = new Dictionary<string, object?> { ["empty"] = "" };
        byte[] cbor = CborEncoder.Encode(input);
        var result = CborDecoder.Decode(cbor);
        Assert.Equal("", result["empty"]);
    }

    [Fact]
    public void Encode_EmptyByteArray_RoundTrips()
    {
        var input = new Dictionary<string, object?> { ["data"] = Array.Empty<byte>() };
        byte[] cbor = CborEncoder.Encode(input);
        var result = CborDecoder.Decode(cbor);
        Assert.Equal(Array.Empty<byte>(), (byte[])result["data"]!);
    }

    [Fact]
    public void Encode_LargeInteger_RoundTrips()
    {
        var input = new Dictionary<string, object?>
        {
            ["max"] = long.MaxValue,
            ["min"] = long.MinValue + 1 // MinValue itself overflows -1-n for uint64
        };

        byte[] cbor = CborEncoder.Encode(input);
        var result = CborDecoder.Decode(cbor);

        Assert.Equal(long.MaxValue, result["max"]);
        Assert.Equal(long.MinValue + 1, result["min"]);
    }

    [Fact]
    public void Encode_IntValues_RoundTrips()
    {
        // int (not long) values should also work
        var input = new Dictionary<string, object?>
        {
            ["small"] = 7,
            ["medium"] = 300
        };

        byte[] cbor = CborEncoder.Encode(input);
        var result = CborDecoder.Decode(cbor);

        // Decoder returns long for all integers
        Assert.Equal(7L, result["small"]);
        Assert.Equal(300L, result["medium"]);
    }

    [Fact]
    public void Encode_FloatValues_RoundTrips()
    {
        // float (not double) values should also work
        var input = new Dictionary<string, object?>
        {
            ["val"] = 1.5f
        };

        byte[] cbor = CborEncoder.Encode(input);
        var result = CborDecoder.Decode(cbor);

        Assert.Equal(1.5, (double)result["val"]!, 5);
    }

    [Fact]
    public void Encode_UnicodeString_RoundTrips()
    {
        var input = new Dictionary<string, object?>
        {
            ["emoji"] = "Hello \U0001F600 World",
            ["cjk"] = "\u4F60\u597D"
        };

        byte[] cbor = CborEncoder.Encode(input);
        var result = CborDecoder.Decode(cbor);

        Assert.Equal("Hello \U0001F600 World", result["emoji"]);
        Assert.Equal("\u4F60\u597D", result["cjk"]);
    }
}
