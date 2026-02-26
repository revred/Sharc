// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Core;
using Sharc.Core.Primitives;
using Sharc.IntegrationTests.Helpers;
using Xunit;

namespace Sharc.IntegrationTests;

public sealed class Fix128IntegrationTests
{
    [Fact]
    public void DeclaredFix128_BlobStorage_ReadsAsDecimal()
    {
        const decimal value = 42.125m;
        byte[] payload = DecimalCodec.Encode(value);

        var data = TestDatabaseFactory.CreateDatabaseWith(conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "CREATE TABLE t (id INTEGER PRIMARY KEY, fx FIX128)";
            cmd.ExecuteNonQuery();

            cmd.CommandText = "INSERT INTO t (fx) VALUES ($v)";
            cmd.Parameters.AddWithValue("$v", payload);
            cmd.ExecuteNonQuery();
        });

        using var db = SharcDatabase.OpenMemory(data);
        using var reader = db.CreateReader("t");
        Assert.True(reader.Read());
        Assert.Equal(value, reader.GetDecimal(1));
    }

    [Fact]
    public void DeclaredFix128_GetGuid_ThrowsInvalidOperation()
    {
        byte[] payload = DecimalCodec.Encode(5.5m);

        var data = TestDatabaseFactory.CreateDatabaseWith(conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "CREATE TABLE t (id INTEGER PRIMARY KEY, fx FIX128)";
            cmd.ExecuteNonQuery();

            cmd.CommandText = "INSERT INTO t (fx) VALUES ($v)";
            cmd.Parameters.AddWithValue("$v", payload);
            cmd.ExecuteNonQuery();
        });

        using var db = SharcDatabase.OpenMemory(data);
        using var reader = db.CreateReader("t");
        Assert.True(reader.Read());
        Assert.Throws<InvalidOperationException>(() => reader.GetGuid(1));
    }

    [Fact]
    public void MergedFix128_WriteReadAndDecimalFilter_Work()
    {
        var data = TestDatabaseFactory.CreateDatabaseWith(conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "CREATE TABLE m (id INTEGER PRIMARY KEY, amount__dhi INTEGER NOT NULL, amount__dlo INTEGER NOT NULL)";
            cmd.ExecuteNonQuery();
        });

        using var db = SharcDatabase.OpenMemory(data, new SharcOpenOptions { Writable = true });
        using var writer = SharcWriter.From(db);

        writer.Insert("m", ColumnValue.FromInt64(1, 1), ColumnValue.FromDecimal(9.99m));
        writer.Insert("m", ColumnValue.FromInt64(1, 2), ColumnValue.FromDecimal(12.50m));

        using var allReader = db.CreateReader("m");
        Assert.True(allReader.Read());
        Assert.Equal(9.99m, allReader.GetDecimal(1));
        Assert.True(allReader.Read());
        Assert.Equal(12.50m, allReader.GetDecimal(1));

        using var filtered = db.CreateReader("m", FilterStar.Column("amount").Gt(10m));
        Assert.True(filtered.Read());
        Assert.Equal(2L, filtered.GetInt64(0));
        Assert.Equal(12.50m, filtered.GetDecimal(1));
        Assert.False(filtered.Read());
    }
}
