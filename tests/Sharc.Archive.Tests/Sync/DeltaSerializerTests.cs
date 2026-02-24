// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Archive.Sync;
using Xunit;

namespace Sharc.Archive.Tests.Sync;

public class DeltaSerializerTests
{
    [Fact]
    public void RoundTrip_MultipleDeltas_Preserved()
    {
        var deltas = new List<byte[]>
        {
            new byte[] { 1, 2, 3 },
            new byte[] { 10, 20, 30, 40, 50 },
            new byte[] { 255 }
        };

        var payload = DeltaSerializer.Serialize(deltas);
        var result = DeltaSerializer.Deserialize(payload);

        Assert.Equal(3, result.Count);
        Assert.Equal(deltas[0], result[0]);
        Assert.Equal(deltas[1], result[1]);
        Assert.Equal(deltas[2], result[2]);
    }

    [Fact]
    public void RoundTrip_EmptyList_Preserved()
    {
        var deltas = new List<byte[]>();

        var payload = DeltaSerializer.Serialize(deltas);
        var result = DeltaSerializer.Deserialize(payload);

        Assert.Empty(result);
    }

    [Fact]
    public void Deserialize_TruncatedPayload_Throws()
    {
        Assert.Throws<ArgumentException>(() => DeltaSerializer.Deserialize(new byte[] { 1 }));
    }

    [Fact]
    public void Serialize_SingleDelta_CorrectFormat()
    {
        var deltas = new List<byte[]> { new byte[] { 0xAA, 0xBB } };

        var payload = DeltaSerializer.Serialize(deltas);

        // [count=1][len=2][0xAA][0xBB] = 4 + 4 + 2 = 10 bytes
        Assert.Equal(10, payload.Length);
    }
}
