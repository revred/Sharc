// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Codec;
using Sharc.Graph.Model;
using Xunit;

namespace Sharc.Graph.Tests.Unit;

/// <summary>
/// C-4: Lazy CBOR decode in GraphRecord.
/// Tests that GraphRecord can hold raw CBOR bytes and support
/// selective field extraction without full deserialization.
/// </summary>
public sealed class LazyCborDecodeTests
{
    [Fact]
    public void GraphRecord_WithJsonData_HasCborDataFalse()
    {
        var record = new GraphRecord(
            new RecordId("test", "id1", new NodeKey(1)),
            new NodeKey(1), 1, "{\"name\":\"test\"}");

        Assert.False(record.HasCborData);
        Assert.Equal("{\"name\":\"test\"}", record.JsonData);
    }

    [Fact]
    public void GraphRecord_WithRawCborData_HasCborDataTrue()
    {
        var cbor = SharcCbor.Encode(new Dictionary<string, object?> { ["name"] = "test" });

        var record = new GraphRecord(
            new RecordId("test", "id1", new NodeKey(1)),
            new NodeKey(1), 1)
        {
            RawCborData = cbor
        };

        Assert.True(record.HasCborData);
    }

    [Fact]
    public void GraphRecord_WithRawCborData_CanExtractField()
    {
        var data = new Dictionary<string, object?> { ["name"] = "Alice", ["age"] = 30L };
        var cbor = SharcCbor.Encode(data);

        var record = new GraphRecord(
            new RecordId("test", "id1", new NodeKey(1)),
            new NodeKey(1), 1)
        {
            RawCborData = cbor
        };

        var name = SharcCbor.ReadField(record.RawCborData!.Value.Span, "name");
        Assert.Equal("Alice", name);
    }

    [Fact]
    public void GraphRecord_WithRawCborData_CanExtractTypedField()
    {
        var data = new Dictionary<string, object?> { ["score"] = 42L };
        var cbor = SharcCbor.Encode(data);

        var record = new GraphRecord(
            new RecordId("test", "id1", new NodeKey(1)),
            new NodeKey(1), 1)
        {
            RawCborData = cbor
        };

        var score = SharcCbor.ReadField<long>(record.RawCborData!.Value.Span, "score");
        Assert.Equal(42L, score);
    }

    [Fact]
    public void GraphRecord_WithRawCborData_MissingFieldReturnsNull()
    {
        var data = new Dictionary<string, object?> { ["name"] = "Alice" };
        var cbor = SharcCbor.Encode(data);

        var record = new GraphRecord(
            new RecordId("test", "id1", new NodeKey(1)),
            new NodeKey(1), 1)
        {
            RawCborData = cbor
        };

        var missing = SharcCbor.ReadField(record.RawCborData!.Value.Span, "nonexistent");
        Assert.Null(missing);
    }

    [Fact]
    public void GraphRecord_NoCborNorJson_JsonDataReturnsEmptyObject()
    {
        var record = new GraphRecord(
            new RecordId("test", "id1", new NodeKey(1)),
            new NodeKey(1), 1);

        Assert.False(record.HasCborData);
        Assert.Equal("{}", record.JsonData);
    }
}
