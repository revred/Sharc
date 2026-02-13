using System.Buffers.Binary;
using Sharc.Core.Format;
using Xunit;

namespace Sharc.Tests.Format;

public class WalFrameHeaderTests
{
    [Fact]
    public void Parse_ValidFrame_ParsesAllFields()
    {
        var data = BuildFrameHeader(pageNumber: 5, dbSize: 100, salt1: 0xAA, salt2: 0xBB);

        var frame = WalFrameHeader.Parse(data);

        Assert.Equal(5u, frame.PageNumber);
        Assert.Equal(100u, frame.DbSizeAfterCommit);
        Assert.Equal(0xAAu, frame.Salt1);
        Assert.Equal(0xBBu, frame.Salt2);
    }

    [Fact]
    public void IsCommitFrame_NonZeroDbSize_ReturnsTrue()
    {
        var data = BuildFrameHeader(dbSize: 42);

        var frame = WalFrameHeader.Parse(data);

        Assert.True(frame.IsCommitFrame);
    }

    [Fact]
    public void IsCommitFrame_ZeroDbSize_ReturnsFalse()
    {
        var data = BuildFrameHeader(dbSize: 0);

        var frame = WalFrameHeader.Parse(data);

        Assert.False(frame.IsCommitFrame);
    }

    [Fact]
    public void Parse_Checksum_ParsedCorrectly()
    {
        var data = BuildFrameHeader(checksum1: 0x12345678, checksum2: 0x9ABCDEF0);

        var frame = WalFrameHeader.Parse(data);

        Assert.Equal(0x12345678u, frame.Checksum1);
        Assert.Equal(0x9ABCDEF0u, frame.Checksum2);
    }

    [Fact]
    public void Parse_TooShort_Throws()
    {
        var data = new byte[20];

        Assert.Throws<ArgumentException>(() => WalFrameHeader.Parse(data));
    }

    private static byte[] BuildFrameHeader(
        uint pageNumber = 1,
        uint dbSize = 0,
        uint salt1 = 0,
        uint salt2 = 0,
        uint checksum1 = 0,
        uint checksum2 = 0)
    {
        var data = new byte[24];
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(0), pageNumber);
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(4), dbSize);
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(8), salt1);
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(12), salt2);
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(16), checksum1);
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(20), checksum2);
        return data;
    }
}
