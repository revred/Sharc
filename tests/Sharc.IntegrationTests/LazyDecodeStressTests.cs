// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.IntegrationTests.Helpers;
using Xunit;

namespace Sharc.IntegrationTests;

/// <summary>
/// Stress tests for SharcDataReader lazy decode — verifies generation counter correctness,
/// column projection, type coercion, repeated column access, and accessor behavior
/// across Read() boundaries.
/// </summary>
public sealed class LazyDecodeStressTests
{
    private static byte[] CreateMixedTypeDatabase()
    {
        return TestDatabaseFactory.CreateDatabaseWith(conn =>
        {
            TestDatabaseFactory.Execute(conn, "CREATE TABLE mixed (id INTEGER PRIMARY KEY, int_val INTEGER, real_val REAL, text_val TEXT, blob_val BLOB)");

            for (int i = 1; i <= 100; i++)
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "INSERT INTO mixed (int_val, real_val, text_val, blob_val) VALUES ($i, $r, $t, $b)";
                cmd.Parameters.AddWithValue("$i", (long)i * 100);
                cmd.Parameters.AddWithValue("$r", i * 1.5);
                cmd.Parameters.AddWithValue("$t", $"row_{i:D5}");
                cmd.Parameters.AddWithValue("$b", new byte[] { (byte)(i & 0xFF), 0xAB });
                cmd.ExecuteNonQuery();
            }
        });
    }

    [Fact]
    public void ReadSameColumn_TwicePerRow_ReturnsSameValue()
    {
        var data = CreateMixedTypeDatabase();
        using var db = SharcDatabase.OpenMemory(data);
        using var reader = db.CreateReader("mixed", "int_val", "text_val");

        int count = 0;
        while (reader.Read())
        {
            long first = reader.GetInt64(0);
            long second = reader.GetInt64(0); // same column again
            Assert.Equal(first, second);

            string t1 = reader.GetString(1);
            string t2 = reader.GetString(1);
            Assert.Equal(t1, t2);
            count++;
        }
        Assert.Equal(100, count);
    }

    [Fact]
    public void ColumnProjection_OnlyRequestedColumnsDecoded()
    {
        var data = CreateMixedTypeDatabase();
        using var db = SharcDatabase.OpenMemory(data);
        // Request only text_val and real_val
        using var reader = db.CreateReader("mixed", "text_val", "real_val");

        Assert.True(reader.Read());
        string textVal = reader.GetString(0);
        double realVal = reader.GetDouble(1);

        Assert.StartsWith("row_", textVal);
        Assert.True(realVal > 0);
    }

    [Fact]
    public void ReadAllTypes_PerRow_CorrectValues()
    {
        var data = CreateMixedTypeDatabase();
        using var db = SharcDatabase.OpenMemory(data);
        using var reader = db.CreateReader("mixed", "int_val", "real_val", "text_val", "blob_val");

        int rowNum = 0;
        while (reader.Read())
        {
            rowNum++;
            long intVal = reader.GetInt64(0);
            double realVal = reader.GetDouble(1);
            string textVal = reader.GetString(2);
            var blobVal = reader.GetBlob(3);

            Assert.Equal((long)rowNum * 100, intVal);
            Assert.Equal(rowNum * 1.5, realVal, 5);
            Assert.Equal($"row_{rowNum:D5}", textVal);
            Assert.Equal((byte)(rowNum & 0xFF), blobVal[0]);
        }
        Assert.Equal(100, rowNum);
    }

    [Fact]
    public void FullScan_NoProjection_AllColumnsAccessible()
    {
        var data = CreateMixedTypeDatabase();
        using var db = SharcDatabase.OpenMemory(data);
        using var reader = db.CreateReader("mixed");

        int count = 0;
        while (reader.Read())
        {
            // Access columns in reverse order
            var blob = reader.GetBlob(4);
            string text = reader.GetString(3);
            double real = reader.GetDouble(2);
            long intV = reader.GetInt64(1);
            long id = reader.GetInt64(0);

            Assert.True(id > 0);
            Assert.True(intV > 0);
            Assert.True(real > 0);
            Assert.StartsWith("row_", text);
            Assert.NotEmpty(blob);
            count++;
        }
        Assert.Equal(100, count);
    }

    [Fact]
    public void NullColumn_IsNull_ReturnsTrue()
    {
        var data = TestDatabaseFactory.CreateAllTypesDatabase();
        using var db = SharcDatabase.OpenMemory(data);
        using var reader = db.CreateReader("all_types", "null_val");

        while (reader.Read())
        {
            Assert.True(reader.IsNull(0));
        }
    }

    [Fact]
    public void Seek_ThenReadColumns_CorrectValues()
    {
        var data = CreateMixedTypeDatabase();
        using var db = SharcDatabase.OpenMemory(data);
        using var reader = db.CreateReader("mixed", "int_val", "text_val");

        Assert.True(reader.Seek(50));
        Assert.Equal(5000L, reader.GetInt64(0));
        Assert.Equal("row_00050", reader.GetString(1));
    }

    [Fact]
    public void Seek_ThenMoveNext_DecodesNewRow()
    {
        var data = CreateMixedTypeDatabase();
        using var db = SharcDatabase.OpenMemory(data);
        using var reader = db.CreateReader("mixed", "int_val");

        Assert.True(reader.Seek(10));
        Assert.Equal(1000L, reader.GetInt64(0));

        // Advance to row 11
        Assert.True(reader.Read());
        Assert.Equal(1100L, reader.GetInt64(0));
    }

    [Fact]
    public void LargeResultSet_AllValuesCorrect()
    {
        var data = TestDatabaseFactory.CreateLargeDatabase(500);
        using var db = SharcDatabase.OpenMemory(data);
        using var reader = db.CreateReader("large_table", "value", "number");

        int count = 0;
        while (reader.Read())
        {
            string val = reader.GetString(0);
            long num = reader.GetInt64(1);
            Assert.NotEmpty(val);
            Assert.True(num > 0);
            count++;
        }
        Assert.Equal(500, count);
    }

    [Fact]
    public void MultipleReaders_SameTable_Independent()
    {
        var data = CreateMixedTypeDatabase();
        using var db = SharcDatabase.OpenMemory(data);

        using var reader1 = db.CreateReader("mixed", "int_val");
        using var reader2 = db.CreateReader("mixed", "text_val");

        // Advance reader1 to row 10
        for (int i = 0; i < 10; i++)
            Assert.True(reader1.Read());
        Assert.Equal(1000L, reader1.GetInt64(0));

        // reader2 should start from row 1
        Assert.True(reader2.Read());
        Assert.Equal("row_00001", reader2.GetString(0));
    }

    [Fact]
    public void SingleColumnProjection_SkipsAllOtherColumns()
    {
        var data = CreateMixedTypeDatabase();
        using var db = SharcDatabase.OpenMemory(data);
        // Only request the last column
        using var reader = db.CreateReader("mixed", "blob_val");

        int count = 0;
        while (reader.Read())
        {
            var blob = reader.GetBlob(0);
            Assert.NotEmpty(blob);
            count++;
        }
        Assert.Equal(100, count);
    }
}
