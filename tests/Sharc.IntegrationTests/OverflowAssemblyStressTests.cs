// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Core;
using Sharc.IntegrationTests.Helpers;
using Xunit;

namespace Sharc.IntegrationTests;

/// <summary>
/// Stress tests for overflow page assembly — verifies that large payloads spanning
/// multiple overflow pages are correctly written, read back, updated, and deleted.
/// </summary>
public sealed class OverflowAssemblyStressTests : IDisposable
{
    private readonly string _dbPath;

    public OverflowAssemblyStressTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"overflow_stress_{Guid.NewGuid()}.db");
    }

    [Fact]
    public void LargeText_4KB_RoundtripPreserved()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(0);
        File.WriteAllBytes(_dbPath, data);

        string bigName = new('Z', 4000);

        using (var db = SharcDatabase.Open(_dbPath, new SharcOpenOptions { Writable = true }))
        {
            using var writer = SharcWriter.From(db);
            writer.Insert("users",
                ColumnValue.FromInt64(1, 1),
                ColumnValue.Text(2 * System.Text.Encoding.UTF8.GetByteCount(bigName) + 13,
                    System.Text.Encoding.UTF8.GetBytes(bigName)),
                ColumnValue.FromInt64(2, 25),
                ColumnValue.FromDouble(100.0),
                ColumnValue.Null());
        }

        using (var db = SharcDatabase.Open(_dbPath))
        {
            using var reader = db.CreateReader("users", "name");
            Assert.True(reader.Read());
            string readName = reader.GetString(0);
            Assert.Equal(4000, readName.Length);
            Assert.Equal(bigName, readName);
        }
    }

    [Fact]
    public void MultipleOverflowRows_AllReadable()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(0);
        File.WriteAllBytes(_dbPath, data);

        const int rowCount = 20;
        const int textSize = 3000;

        using (var db = SharcDatabase.Open(_dbPath, new SharcOpenOptions { Writable = true }))
        {
            using var writer = SharcWriter.From(db);
            for (int i = 1; i <= rowCount; i++)
            {
                string bigVal = new((char)('A' + (i % 26)), textSize);
                writer.Insert("users",
                    ColumnValue.FromInt64(1, i),
                    ColumnValue.Text(2 * System.Text.Encoding.UTF8.GetByteCount(bigVal) + 13,
                        System.Text.Encoding.UTF8.GetBytes(bigVal)),
                    ColumnValue.FromInt64(2, i),
                    ColumnValue.FromDouble(i * 1.5),
                    ColumnValue.Null());
            }
        }

        using (var db = SharcDatabase.Open(_dbPath))
        {
            using var reader = db.CreateReader("users", "name", "age");
            int count = 0;
            while (reader.Read())
            {
                count++;
                string name = reader.GetString(0);
                Assert.Equal(textSize, name.Length);
            }
            Assert.Equal(rowCount, count);
        }
    }

    [Fact]
    public void VeryLargePayload_MultiOverflowPages_Correct()
    {
        // 20KB text = ~5 overflow pages at 4096 page size
        var data = TestDatabaseFactory.CreateUsersDatabase(0);
        File.WriteAllBytes(_dbPath, data);

        string hugeVal = new('Q', 20000);

        using (var db = SharcDatabase.Open(_dbPath, new SharcOpenOptions { Writable = true }))
        {
            using var writer = SharcWriter.From(db);
            writer.Insert("users",
                ColumnValue.FromInt64(1, 1),
                ColumnValue.Text(2 * System.Text.Encoding.UTF8.GetByteCount(hugeVal) + 13,
                    System.Text.Encoding.UTF8.GetBytes(hugeVal)),
                ColumnValue.FromInt64(2, 25),
                ColumnValue.FromDouble(100.0),
                ColumnValue.Null());
        }

        using (var db = SharcDatabase.Open(_dbPath))
        {
            using var reader = db.CreateReader("users", "name");
            Assert.True(reader.Read());
            Assert.Equal(hugeVal, reader.GetString(0));
        }
    }

    [Fact]
    public void VeryLargePayload_50KB_MultiOverflowChain()
    {
        // 50KB text = ~13 overflow pages at 4096 page size
        var data = TestDatabaseFactory.CreateUsersDatabase(0);
        File.WriteAllBytes(_dbPath, data);

        string hugeVal = new('M', 50000);

        using (var db = SharcDatabase.Open(_dbPath, new SharcOpenOptions { Writable = true }))
        {
            using var writer = SharcWriter.From(db);
            writer.Insert("users",
                ColumnValue.FromInt64(1, 1),
                ColumnValue.Text(2 * System.Text.Encoding.UTF8.GetByteCount(hugeVal) + 13,
                    System.Text.Encoding.UTF8.GetBytes(hugeVal)),
                ColumnValue.FromInt64(2, 30),
                ColumnValue.FromDouble(50.0),
                ColumnValue.Null());
        }

        using (var db = SharcDatabase.Open(_dbPath))
        {
            using var reader = db.CreateReader("users", "name");
            Assert.True(reader.Read());
            Assert.Equal(hugeVal, reader.GetString(0));
        }
    }

    [Fact]
    public void MultipleVeryLargeRows_AllReadable()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(0);
        File.WriteAllBytes(_dbPath, data);

        const int rowCount = 5;
        const int textSize = 15000;

        using (var db = SharcDatabase.Open(_dbPath, new SharcOpenOptions { Writable = true }))
        {
            using var writer = SharcWriter.From(db);
            for (int i = 1; i <= rowCount; i++)
            {
                string bigVal = new((char)('A' + (i % 26)), textSize);
                writer.Insert("users",
                    ColumnValue.FromInt64(1, i),
                    ColumnValue.Text(2 * System.Text.Encoding.UTF8.GetByteCount(bigVal) + 13,
                        System.Text.Encoding.UTF8.GetBytes(bigVal)),
                    ColumnValue.FromInt64(2, i),
                    ColumnValue.FromDouble(i * 1.5),
                    ColumnValue.Null());
            }
        }

        using (var db = SharcDatabase.Open(_dbPath))
        {
            using var reader = db.CreateReader("users", "name");
            int count = 0;
            while (reader.Read())
            {
                count++;
                string name = reader.GetString(0);
                Assert.Equal(textSize, name.Length);
            }
            Assert.Equal(rowCount, count);
        }
    }

    [Fact]
    public void UpdateToLargerOverflow_MultiChain_Correct()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(0);
        File.WriteAllBytes(_dbPath, data);

        using (var db = SharcDatabase.Open(_dbPath, new SharcOpenOptions { Writable = true }))
        {
            using var writer = SharcWriter.From(db);

            // Insert a moderately large row (single overflow)
            string oldVal = new('A', 4000);
            writer.Insert("users",
                ColumnValue.FromInt64(1, 1),
                ColumnValue.Text(2 * System.Text.Encoding.UTF8.GetByteCount(oldVal) + 13,
                    System.Text.Encoding.UTF8.GetBytes(oldVal)),
                ColumnValue.FromInt64(2, 25),
                ColumnValue.FromDouble(100.0),
                ColumnValue.Null());

            // Update to a much larger row (multi-overflow)
            string newVal = new('B', 20000);
            writer.Update("users", 1,
                ColumnValue.FromInt64(1, 1),
                ColumnValue.Text(2 * System.Text.Encoding.UTF8.GetByteCount(newVal) + 13,
                    System.Text.Encoding.UTF8.GetBytes(newVal)),
                ColumnValue.FromInt64(2, 30),
                ColumnValue.FromDouble(200.0),
                ColumnValue.Null());
        }

        using (var db = SharcDatabase.Open(_dbPath))
        {
            using var reader = db.CreateReader("users", "name");
            Assert.True(reader.Read());
            string result = reader.GetString(0);
            Assert.Equal(20000, result.Length);
            Assert.True(result.All(c => c == 'B'));
        }
    }

    [Fact]
    public void OverflowRow_DeleteThenInsertNew_FreelistRecyclesPages()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(0);
        File.WriteAllBytes(_dbPath, data);

        string bigVal = new('X', 3500);

        using (var db = SharcDatabase.Open(_dbPath, new SharcOpenOptions { Writable = true }))
        {
            using var writer = SharcWriter.From(db);

            // Insert overflow row
            writer.Insert("users",
                ColumnValue.FromInt64(1, 1),
                ColumnValue.Text(2 * System.Text.Encoding.UTF8.GetByteCount(bigVal) + 13,
                    System.Text.Encoding.UTF8.GetBytes(bigVal)),
                ColumnValue.FromInt64(2, 25),
                ColumnValue.FromDouble(100.0),
                ColumnValue.Null());

            // Delete it — overflow pages should go to freelist
            writer.Delete("users", 1);

            // Insert a new overflow row — should reuse freed pages
            string newVal = new('Y', 3500);
            writer.Insert("users",
                ColumnValue.FromInt64(1, 2),
                ColumnValue.Text(2 * System.Text.Encoding.UTF8.GetByteCount(newVal) + 13,
                    System.Text.Encoding.UTF8.GetBytes(newVal)),
                ColumnValue.FromInt64(2, 30),
                ColumnValue.FromDouble(200.0),
                ColumnValue.Null());
        }

        using (var db = SharcDatabase.Open(_dbPath))
        {
            using var reader = db.CreateReader("users", "name");
            Assert.True(reader.Read());
            string result = reader.GetString(0);
            Assert.Equal(3500, result.Length);
            Assert.True(result.All(c => c == 'Y'));
            Assert.False(reader.Read()); // only 1 row
        }
    }

    [Fact]
    public void UpdateOverflowRow_NewValueCorrect()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(0);
        File.WriteAllBytes(_dbPath, data);

        using (var db = SharcDatabase.Open(_dbPath, new SharcOpenOptions { Writable = true }))
        {
            using var writer = SharcWriter.From(db);

            string oldVal = new('A', 2000);
            writer.Insert("users",
                ColumnValue.FromInt64(1, 1),
                ColumnValue.Text(2 * System.Text.Encoding.UTF8.GetByteCount(oldVal) + 13,
                    System.Text.Encoding.UTF8.GetBytes(oldVal)),
                ColumnValue.FromInt64(2, 25),
                ColumnValue.FromDouble(100.0),
                ColumnValue.Null());

            string newVal = new('B', 3500);
            writer.Update("users", 1,
                ColumnValue.FromInt64(1, 1),
                ColumnValue.Text(2 * System.Text.Encoding.UTF8.GetByteCount(newVal) + 13,
                    System.Text.Encoding.UTF8.GetBytes(newVal)),
                ColumnValue.FromInt64(2, 30),
                ColumnValue.FromDouble(200.0),
                ColumnValue.Null());
        }

        using (var db = SharcDatabase.Open(_dbPath))
        {
            using var reader = db.CreateReader("users", "name");
            Assert.True(reader.Read());
            string result = reader.GetString(0);
            Assert.Equal(3500, result.Length);
            Assert.True(result.All(c => c == 'B'));
        }
    }

    [Fact]
    public void MixedOverflowAndInline_AllRowsReadable()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(0);
        File.WriteAllBytes(_dbPath, data);

        using (var db = SharcDatabase.Open(_dbPath, new SharcOpenOptions { Writable = true }))
        {
            using var writer = SharcWriter.From(db);
            for (int i = 1; i <= 50; i++)
            {
                // Alternate: small inline rows and large overflow rows
                int textLen = i % 2 == 0 ? 10 : 3500;
                string val = new((char)('A' + (i % 26)), textLen);
                writer.Insert("users",
                    ColumnValue.FromInt64(1, i),
                    ColumnValue.Text(2 * System.Text.Encoding.UTF8.GetByteCount(val) + 13,
                        System.Text.Encoding.UTF8.GetBytes(val)),
                    ColumnValue.FromInt64(2, i),
                    ColumnValue.FromDouble(i * 1.0),
                    ColumnValue.Null());
            }
        }

        using (var db = SharcDatabase.Open(_dbPath))
        {
            using var reader = db.CreateReader("users", "name", "age");
            int count = 0;
            while (reader.Read())
            {
                count++;
                string name = reader.GetString(0);
                long age = reader.GetInt64(1);
                int expectedLen = age % 2 == 0 ? 10 : 3500;
                Assert.Equal(expectedLen, name.Length);
            }
            Assert.Equal(50, count);
        }
    }

    [Fact]
    public void OverflowRows_SeekByRowid_Correct()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(0);
        File.WriteAllBytes(_dbPath, data);

        using (var db = SharcDatabase.Open(_dbPath, new SharcOpenOptions { Writable = true }))
        {
            using var writer = SharcWriter.From(db);
            for (int i = 1; i <= 10; i++)
            {
                string val = $"overflow_row_{i}_" + new string('X', 4000);
                writer.Insert("users",
                    ColumnValue.FromInt64(1, i),
                    ColumnValue.Text(2 * System.Text.Encoding.UTF8.GetByteCount(val) + 13,
                        System.Text.Encoding.UTF8.GetBytes(val)),
                    ColumnValue.FromInt64(2, i),
                    ColumnValue.FromDouble(i * 1.0),
                    ColumnValue.Null());
            }
        }

        using (var db = SharcDatabase.Open(_dbPath))
        {
            using var reader = db.CreateReader("users", "name");
            // Seek to specific overflow row
            Assert.True(reader.Seek(5));
            string name = reader.GetString(0);
            Assert.StartsWith("overflow_row_5_", name);
            Assert.Equal(4000 + "overflow_row_5_".Length, name.Length);
        }
    }

    public void Dispose()
    {
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
        GC.SuppressFinalize(this);
    }
}
