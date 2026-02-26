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

using Sharc.Core;
using Sharc.Core.Primitives;
using Sharc.Core.Schema;
using System.Buffers.Binary;
using Xunit;

namespace Sharc.Tests.Write;

/// <summary>
/// TDD tests for SharcWriter.ExpandMergedColumns — expands logical ColumnValue[]
/// (with UniqueId GUID values) into physical ColumnValue[] (with hi/lo Int64 pairs).
/// </summary>
public sealed class ExpandMergedColumnsTests
{
    /// <summary>
    /// Helper to build a TableInfo with the given columns and physical column count.
    /// </summary>
    private static TableInfo MakeTable(IReadOnlyList<ColumnInfo> columns, int physicalColumnCount)
    {
        var table = new TableInfo
        {
            Name = "test",
            RootPage = 2,
            Sql = "CREATE TABLE test (...)",
            Columns = columns,
            IsWithoutRowId = false
        };
        table.PhysicalColumnCount = physicalColumnCount;
        return table;
    }

    [Fact]
    public void ExpandMergedColumns_NoMergedColumns_ReturnsSameArray()
    {
        // Table with no merged columns: id INTEGER, name TEXT
        var columns = new[]
        {
            new ColumnInfo { Name = "id", DeclaredType = "INTEGER", Ordinal = 0, IsPrimaryKey = true, IsNotNull = true },
            new ColumnInfo { Name = "name", DeclaredType = "TEXT", Ordinal = 1, IsPrimaryKey = false, IsNotNull = false }
        };
        var table = MakeTable(columns, 2);

        var logical = new[] { ColumnValue.FromInt64(6, 1), ColumnValue.Text(15, "Alice"u8.ToArray()) };
        var result = SharcWriter.ExpandMergedColumns(logical, table);

        Assert.Same(logical, result); // fast path: returns same reference
    }

    [Fact]
    public void ExpandMergedColumns_WithMergedGuid_ExpandsToTwoInt64s()
    {
        // Table: id INTEGER, owner_guid (merged from __hi/__lo)
        // Logical columns: [id, owner_guid]
        // Physical columns: [id, owner_guid__hi, owner_guid__lo]
        var columns = new[]
        {
            new ColumnInfo { Name = "id", DeclaredType = "INTEGER", Ordinal = 0, IsPrimaryKey = true, IsNotNull = true,
                MergedPhysicalOrdinals = [0] },
            new ColumnInfo { Name = "owner_guid", DeclaredType = "GUID", Ordinal = 1, IsPrimaryKey = false, IsNotNull = true,
                IsGuidColumn = true, MergedPhysicalOrdinals = [1, 2] }
        };
        var table = MakeTable(columns, 3);

        var guid = new Guid("01020304-0506-0708-090a-0b0c0d0e0f10");
        var logical = new[] { ColumnValue.FromInt64(6, 1), ColumnValue.FromGuid(guid) };
        var result = SharcWriter.ExpandMergedColumns(logical, table);

        Assert.Equal(3, result.Length);
        Assert.Equal(1L, result[0].AsInt64()); // id unchanged
        Assert.Equal(ColumnStorageClass.Integral, result[1].StorageClass); // hi
        Assert.Equal(0x0102030405060708L, result[1].AsInt64());
        Assert.Equal(ColumnStorageClass.Integral, result[2].StorageClass); // lo
        Assert.Equal(0x090a0b0c0d0e0f10L, result[2].AsInt64());
    }

    [Fact]
    public void ExpandMergedColumns_MixedColumns_CorrectExpansion()
    {
        // Table: id INTEGER, owner_guid (merged), name TEXT
        // Logical: [id, owner_guid, name] → Physical: [id, owner_guid__hi, owner_guid__lo, name]
        var columns = new[]
        {
            new ColumnInfo { Name = "id", DeclaredType = "INTEGER", Ordinal = 0, IsPrimaryKey = true, IsNotNull = true,
                MergedPhysicalOrdinals = [0] },
            new ColumnInfo { Name = "owner_guid", DeclaredType = "GUID", Ordinal = 1, IsPrimaryKey = false, IsNotNull = true,
                IsGuidColumn = true, MergedPhysicalOrdinals = [1, 2] },
            new ColumnInfo { Name = "name", DeclaredType = "TEXT", Ordinal = 2, IsPrimaryKey = false, IsNotNull = false,
                MergedPhysicalOrdinals = [3] }
        };
        var table = MakeTable(columns, 4);

        var guid = Guid.NewGuid();
        var logical = new[]
        {
            ColumnValue.FromInt64(6, 42),
            ColumnValue.FromGuid(guid),
            ColumnValue.Text(17, "Bob"u8.ToArray())
        };
        var result = SharcWriter.ExpandMergedColumns(logical, table);

        Assert.Equal(4, result.Length);
        Assert.Equal(42L, result[0].AsInt64());
        Assert.Equal(ColumnStorageClass.Integral, result[1].StorageClass);
        Assert.Equal(ColumnStorageClass.Integral, result[2].StorageClass);
        Assert.Equal("Bob", result[3].AsString());

        // Verify the hi/lo values reconstruct the original GUID
        var restored = Core.Primitives.GuidCodec.FromInt64Pair(result[1].AsInt64(), result[2].AsInt64());
        Assert.Equal(guid, restored);
    }

    [Fact]
    public void ExpandMergedColumns_MultipleGuidColumns_AllExpanded()
    {
        // Table: id INTEGER, guid_a (merged), guid_b (merged)
        // Logical: [id, guid_a, guid_b] → Physical: [id, guid_a__hi, guid_a__lo, guid_b__hi, guid_b__lo]
        var columns = new[]
        {
            new ColumnInfo { Name = "id", DeclaredType = "INTEGER", Ordinal = 0, IsPrimaryKey = true, IsNotNull = true,
                MergedPhysicalOrdinals = [0] },
            new ColumnInfo { Name = "guid_a", DeclaredType = "GUID", Ordinal = 1, IsPrimaryKey = false, IsNotNull = true,
                IsGuidColumn = true, MergedPhysicalOrdinals = [1, 2] },
            new ColumnInfo { Name = "guid_b", DeclaredType = "GUID", Ordinal = 2, IsPrimaryKey = false, IsNotNull = true,
                IsGuidColumn = true, MergedPhysicalOrdinals = [3, 4] }
        };
        var table = MakeTable(columns, 5);

        var guidA = Guid.NewGuid();
        var guidB = Guid.NewGuid();
        var logical = new[]
        {
            ColumnValue.FromInt64(6, 1),
            ColumnValue.FromGuid(guidA),
            ColumnValue.FromGuid(guidB)
        };
        var result = SharcWriter.ExpandMergedColumns(logical, table);

        Assert.Equal(5, result.Length);
        Assert.Equal(1L, result[0].AsInt64());

        var restoredA = Core.Primitives.GuidCodec.FromInt64Pair(result[1].AsInt64(), result[2].AsInt64());
        Assert.Equal(guidA, restoredA);

        var restoredB = Core.Primitives.GuidCodec.FromInt64Pair(result[3].AsInt64(), result[4].AsInt64());
        Assert.Equal(guidB, restoredB);
    }

    [Fact]
    public void ExpandMergedColumns_NullGuid_ExpandsToTwoNulls()
    {
        // When the GUID value is NULL, both hi and lo should be NULL
        var columns = new[]
        {
            new ColumnInfo { Name = "id", DeclaredType = "INTEGER", Ordinal = 0, IsPrimaryKey = true, IsNotNull = true,
                MergedPhysicalOrdinals = [0] },
            new ColumnInfo { Name = "owner_guid", DeclaredType = "GUID", Ordinal = 1, IsPrimaryKey = false, IsNotNull = false,
                IsGuidColumn = true, MergedPhysicalOrdinals = [1, 2] }
        };
        var table = MakeTable(columns, 3);

        var logical = new[] { ColumnValue.FromInt64(6, 1), ColumnValue.Null() };
        var result = SharcWriter.ExpandMergedColumns(logical, table);

        Assert.Equal(3, result.Length);
        Assert.True(result[1].IsNull);
        Assert.True(result[2].IsNull);
    }

    [Fact]
    public void ExpandMergedColumns_WithMergedDecimal_ExpandsToTwoInt64s()
    {
        var columns = new[]
        {
            new ColumnInfo { Name = "id", DeclaredType = "INTEGER", Ordinal = 0, IsPrimaryKey = true, IsNotNull = true,
                MergedPhysicalOrdinals = [0] },
            new ColumnInfo { Name = "price", DeclaredType = "FIX128", Ordinal = 1, IsPrimaryKey = false, IsNotNull = true,
                IsDecimalColumn = true, MergedPhysicalOrdinals = [1, 2] }
        };
        var table = MakeTable(columns, 3);

        const decimal amount = 1234.5600m;
        var logical = new[] { ColumnValue.FromInt64(6, 1), ColumnValue.FromDecimal(amount) };
        var result = SharcWriter.ExpandMergedColumns(logical, table);

        Assert.Equal(3, result.Length);
        Assert.Equal(ColumnStorageClass.Integral, result[1].StorageClass);
        Assert.Equal(ColumnStorageClass.Integral, result[2].StorageClass);

        Span<byte> payload = stackalloc byte[DecimalCodec.ByteCount];
        BinaryPrimitives.WriteInt64BigEndian(payload[..8], result[1].AsInt64());
        BinaryPrimitives.WriteInt64BigEndian(payload.Slice(8, 8), result[2].AsInt64());
        Assert.Equal(DecimalCodec.Normalize(amount), DecimalCodec.Decode(payload));
    }
}
