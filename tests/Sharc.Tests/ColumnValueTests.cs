using FluentAssertions;
using Sharc.Core;
using Xunit;

namespace Sharc.Tests;

/// <summary>
/// TDD tests for ColumnValue struct.
/// </summary>
public class ColumnValueTests
{
    [Fact]
    public void Null_IsNullReturnsTrue()
    {
        var val = ColumnValue.Null();
        val.IsNull.Should().BeTrue();
        val.StorageClass.Should().Be(ColumnStorageClass.Null);
    }

    [Fact]
    public void Integer_StoresAndRetrievesValue()
    {
        var val = ColumnValue.Integer(4, 42);
        val.IsNull.Should().BeFalse();
        val.StorageClass.Should().Be(ColumnStorageClass.Integer);
        val.AsInt64().Should().Be(42);
    }

    [Fact]
    public void Integer_NegativeValue_Works()
    {
        var val = ColumnValue.Integer(6, -9999);
        val.AsInt64().Should().Be(-9999);
    }

    [Fact]
    public void Float_StoresAndRetrievesValue()
    {
        var val = ColumnValue.Float(3.14159);
        val.StorageClass.Should().Be(ColumnStorageClass.Float);
        val.AsDouble().Should().BeApproximately(3.14159, 0.00001);
    }

    [Fact]
    public void Text_StoresAndRetrievesString()
    {
        var bytes = "Hello, Sharc!"u8.ToArray();
        var val = ColumnValue.Text(39, bytes); // serial type for 13-char text: (13*2)+13 = 39
        val.StorageClass.Should().Be(ColumnStorageClass.Text);
        val.AsString().Should().Be("Hello, Sharc!");
    }

    [Fact]
    public void Blob_StoresAndRetrievesBytes()
    {
        byte[] data = [0xDE, 0xAD, 0xBE, 0xEF];
        var val = ColumnValue.Blob(20, data); // serial type for 4-byte blob: (4*2)+12 = 20
        val.StorageClass.Should().Be(ColumnStorageClass.Blob);
        val.AsBytes().Span.SequenceEqual(data).Should().BeTrue();
    }

    [Fact]
    public void Integer_ConstantZero_SerialType8()
    {
        var val = ColumnValue.Integer(8, 0);
        val.SerialType.Should().Be(8);
        val.AsInt64().Should().Be(0);
    }

    [Fact]
    public void Integer_ConstantOne_SerialType9()
    {
        var val = ColumnValue.Integer(9, 1);
        val.SerialType.Should().Be(9);
        val.AsInt64().Should().Be(1);
    }
}
