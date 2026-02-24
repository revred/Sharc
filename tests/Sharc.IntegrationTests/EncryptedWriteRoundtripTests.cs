// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using System.Security.Cryptography;
using System.Text;
using Sharc.Core;
using Sharc.Crypto;
using Xunit;

namespace Sharc.IntegrationTests;

/// <summary>
/// TD-5: Encrypted write roundtrip integration tests.
/// Verifies that data written via SharcWriter to a SharcDatabase.Create database
/// survives page-level encryption and can be read back with the correct password.
/// Tests cover single-row, multi-row, multi-table, multi-page, batch, update, delete,
/// and mixed-type scenarios.
/// </summary>
public sealed class EncryptedWriteRoundtripTests : IDisposable
{
    private readonly string _tempDir;
    private const string TestPassword = "correct-horse-battery-staple";

    public EncryptedWriteRoundtripTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"sharc_enc_write_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    [Fact]
    public void Roundtrip_SingleRow_TextColumns()
    {
        var plainPath = CreateAndPopulate("single_text", db =>
        {
            using var tx = db.BeginTransaction();
            tx.Execute("CREATE TABLE notes (id INTEGER PRIMARY KEY, title TEXT, body TEXT)");
            tx.Commit();

            using var sw = SharcWriter.From(db);
            sw.Insert("notes", TextVal("Hello"), TextVal("World"));
        });

        var encPath = EncryptFile(plainPath);

        using var db = OpenEncrypted(encPath);
        using var reader = db.CreateReader("notes");
        Assert.True(reader.Read());
        Assert.Equal("Hello", reader.GetString(0));
        Assert.Equal("World", reader.GetString(1));
        Assert.False(reader.Read());
    }

    [Fact]
    public void Roundtrip_SingleRow_IntegerColumns()
    {
        var plainPath = CreateAndPopulate("single_int", db =>
        {
            using var tx = db.BeginTransaction();
            tx.Execute("CREATE TABLE counters (id INTEGER PRIMARY KEY, name TEXT, value INTEGER)");
            tx.Commit();

            using var sw = SharcWriter.From(db);
            sw.Insert("counters", TextVal("visits"), ColumnValue.FromInt64(1, 42));
        });

        var encPath = EncryptFile(plainPath);

        using var db = OpenEncrypted(encPath);
        using var reader = db.CreateReader("counters");
        Assert.True(reader.Read());
        Assert.Equal("visits", reader.GetString(0));
        Assert.Equal(42, reader.GetInt64(1));
    }

    [Fact]
    public void Roundtrip_SingleRow_MixedTypes()
    {
        var plainPath = CreateAndPopulate("mixed", db =>
        {
            using var tx = db.BeginTransaction();
            tx.Execute("CREATE TABLE items (id INTEGER PRIMARY KEY, name TEXT, qty INTEGER, price REAL, data BLOB)");
            tx.Commit();

            var blobData = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF };
            using var sw = SharcWriter.From(db);
            sw.Insert("items",
                TextVal("Widget"),
                ColumnValue.FromInt64(1, 100),
                ColumnValue.FromDouble(9.99),
                ColumnValue.Blob(blobData.Length, blobData));
        });

        var encPath = EncryptFile(plainPath);

        using var db = OpenEncrypted(encPath);
        using var reader = db.CreateReader("items");
        Assert.True(reader.Read());
        Assert.Equal("Widget", reader.GetString(0));
        Assert.Equal(100, reader.GetInt64(1));
        Assert.Equal(9.99, reader.GetDouble(2), 2);
        var blob = reader.GetBlob(3);
        Assert.Equal(new byte[] { 0xDE, 0xAD, 0xBE, 0xEF }, blob.ToArray());
    }

    [Fact]
    public void Roundtrip_MultipleRows_PreservesAll()
    {
        var plainPath = CreateAndPopulate("multi_row", db =>
        {
            using var tx = db.BeginTransaction();
            tx.Execute("CREATE TABLE users (id INTEGER PRIMARY KEY, name TEXT, age INTEGER)");
            tx.Commit();

            using var sw = SharcWriter.From(db);
            for (int i = 1; i <= 10; i++)
                sw.Insert("users", TextVal($"User_{i}"), ColumnValue.FromInt64(1, 20 + i));
        });

        var encPath = EncryptFile(plainPath);

        using var db = OpenEncrypted(encPath);
        using var reader = db.CreateReader("users");
        int count = 0;
        while (reader.Read())
        {
            count++;
            Assert.StartsWith("User_", reader.GetString(0));
        }
        Assert.Equal(10, count);
    }

    [Fact]
    public void Roundtrip_MultipleTables_AllReadable()
    {
        var plainPath = CreateAndPopulate("multi_table", db =>
        {
            using var tx = db.BeginTransaction();
            tx.Execute("CREATE TABLE authors (id INTEGER PRIMARY KEY, name TEXT)");
            tx.Execute("CREATE TABLE books (id INTEGER PRIMARY KEY, title TEXT, author_id INTEGER)");
            tx.Execute("CREATE TABLE reviews (id INTEGER PRIMARY KEY, book_id INTEGER, rating INTEGER)");
            tx.Commit();

            using var sw = SharcWriter.From(db);
            sw.Insert("authors", TextVal("Tolkien"));
            sw.Insert("books", TextVal("The Hobbit"), ColumnValue.FromInt64(1, 1));
            sw.Insert("reviews", ColumnValue.FromInt64(1, 1), ColumnValue.FromInt64(1, 5));
        });

        var encPath = EncryptFile(plainPath);

        using var db = OpenEncrypted(encPath);

        using var r1 = db.CreateReader("authors");
        Assert.True(r1.Read());
        Assert.Equal("Tolkien", r1.GetString(0));

        using var r2 = db.CreateReader("books");
        Assert.True(r2.Read());
        Assert.Equal("The Hobbit", r2.GetString(0));

        using var r3 = db.CreateReader("reviews");
        Assert.True(r3.Read());
        Assert.Equal(5, r3.GetInt64(1));
    }

    [Fact]
    public void Roundtrip_MultiPage_LargeDataset()
    {
        // 500 rows with ~200-byte text each → multi-page B-tree
        var plainPath = CreateAndPopulate("multi_page", db =>
        {
            using var tx = db.BeginTransaction();
            tx.Execute("CREATE TABLE logs (id INTEGER PRIMARY KEY, timestamp TEXT, message TEXT, level INTEGER)");
            tx.Commit();

            using var sw = SharcWriter.From(db);
            for (int i = 0; i < 500; i++)
            {
                sw.Insert("logs",
                    TextVal($"2026-01-01T{i / 3600:D2}:{(i / 60) % 60:D2}:{i % 60:D2}"),
                    TextVal($"Log entry {i}: " + new string('X', 150)),
                    ColumnValue.FromInt64(1, i % 4));
            }
        });

        var encPath = EncryptFile(plainPath);

        using var db = OpenEncrypted(encPath);
        using var reader = db.CreateReader("logs");
        int count = 0;
        while (reader.Read())
        {
            count++;
            Assert.NotNull(reader.GetString(0)); // timestamp
            Assert.NotNull(reader.GetString(1)); // message
        }
        Assert.Equal(500, count);
    }

    [Fact]
    public void Roundtrip_BatchInsert_AllRowsPreserved()
    {
        var plainPath = CreateAndPopulate("batch", db =>
        {
            using var tx = db.BeginTransaction();
            tx.Execute("CREATE TABLE metrics (id INTEGER PRIMARY KEY, name TEXT, value INTEGER)");
            tx.Commit();

            using var sw = SharcWriter.From(db);
            var records = Enumerable.Range(0, 50)
                .Select(i => new[]
                {
                    TextVal($"metric_{i}"),
                    ColumnValue.FromInt64(1, i * 10)
                });
            sw.InsertBatch("metrics", records);
        });

        var encPath = EncryptFile(plainPath);

        using var db = OpenEncrypted(encPath);
        using var reader = db.CreateReader("metrics");
        int count = 0;
        while (reader.Read()) count++;
        Assert.Equal(50, count);
    }

    [Fact]
    public void Roundtrip_UpdateThenEncrypt_ReflectsUpdate()
    {
        var plainPath = CreateAndPopulate("update", db =>
        {
            using var tx = db.BeginTransaction();
            tx.Execute("CREATE TABLE settings (id INTEGER PRIMARY KEY, key TEXT, value TEXT)");
            tx.Commit();

            using var sw = SharcWriter.From(db);
            var rowId = sw.Insert("settings", TextVal("theme"), TextVal("light"));
            sw.Update("settings", rowId, TextVal("theme"), TextVal("dark"));
        });

        var encPath = EncryptFile(plainPath);

        using var db = OpenEncrypted(encPath);
        using var reader = db.CreateReader("settings");
        Assert.True(reader.Read());
        Assert.Equal("theme", reader.GetString(0));
        Assert.Equal("dark", reader.GetString(1));
        Assert.False(reader.Read()); // only 1 row
    }

    [Fact]
    public void Roundtrip_DeleteThenEncrypt_RowRemoved()
    {
        var plainPath = CreateAndPopulate("delete", db =>
        {
            using var tx = db.BeginTransaction();
            tx.Execute("CREATE TABLE items (id INTEGER PRIMARY KEY, name TEXT)");
            tx.Commit();

            using var sw = SharcWriter.From(db);
            sw.Insert("items", TextVal("keep"));
            var deleteId = sw.Insert("items", TextVal("remove"));
            sw.Insert("items", TextVal("also_keep"));
            sw.Delete("items", deleteId);
        });

        var encPath = EncryptFile(plainPath);

        using var db = OpenEncrypted(encPath);
        using var reader = db.CreateReader("items");
        var names = new List<string>();
        while (reader.Read())
            names.Add(reader.GetString(0));

        Assert.Equal(2, names.Count);
        Assert.Contains("keep", names);
        Assert.Contains("also_keep", names);
        Assert.DoesNotContain("remove", names);
    }

    [Fact]
    public void Roundtrip_NullableColumns_PreservesNulls()
    {
        var plainPath = CreateAndPopulate("nulls", db =>
        {
            using var tx = db.BeginTransaction();
            tx.Execute("CREATE TABLE contacts (id INTEGER PRIMARY KEY, name TEXT NOT NULL, email TEXT, phone TEXT)");
            tx.Commit();

            using var sw = SharcWriter.From(db);
            sw.Insert("contacts", TextVal("Alice"), TextVal("alice@example.com"), ColumnValue.Null());
            sw.Insert("contacts", TextVal("Bob"), ColumnValue.Null(), TextVal("555-0100"));
        });

        var encPath = EncryptFile(plainPath);

        using var db = OpenEncrypted(encPath);
        using var reader = db.CreateReader("contacts");

        Assert.True(reader.Read());
        Assert.Equal("Alice", reader.GetString(0));
        Assert.False(reader.IsNull(1)); // email present
        Assert.True(reader.IsNull(2));  // phone null

        Assert.True(reader.Read());
        Assert.Equal("Bob", reader.GetString(0));
        Assert.True(reader.IsNull(1));  // email null
        Assert.False(reader.IsNull(2)); // phone present
    }

    [Fact]
    public void Roundtrip_SchemaPreserved_AfterEncryption()
    {
        var plainPath = CreateAndPopulate("schema", db =>
        {
            using var tx = db.BeginTransaction();
            tx.Execute("CREATE TABLE products (id INTEGER PRIMARY KEY, sku TEXT, name TEXT, price REAL, stock INTEGER)");
            tx.Execute("CREATE TABLE categories (id INTEGER PRIMARY KEY, label TEXT)");
            tx.Commit();
        });

        var encPath = EncryptFile(plainPath);

        using var db = OpenEncrypted(encPath);
        var tables = db.Schema.Tables.Select(t => t.Name).ToList();
        Assert.Contains("products", tables);
        Assert.Contains("categories", tables);

        var productCols = db.Schema.GetTable("products")!.Columns.Select(c => c.Name).ToList();
        Assert.Contains("sku", productCols);
        Assert.Contains("name", productCols);
        Assert.Contains("price", productCols);
        Assert.Contains("stock", productCols);
    }

    [Fact]
    public void Roundtrip_EmptyTable_AfterEncryption()
    {
        var plainPath = CreateAndPopulate("empty", db =>
        {
            using var tx = db.BeginTransaction();
            tx.Execute("CREATE TABLE empty_table (id INTEGER PRIMARY KEY, data TEXT)");
            tx.Commit();
        });

        var encPath = EncryptFile(plainPath);

        using var db = OpenEncrypted(encPath);
        Assert.NotNull(db.Schema.GetTable("empty_table"));
        using var reader = db.CreateReader("empty_table");
        Assert.False(reader.Read()); // no rows
    }

    #region Helpers

    private string CreateAndPopulate(string name, Action<SharcDatabase> populate)
    {
        var path = Path.Combine(_tempDir, $"{name}.arc");
        using var db = SharcDatabase.Create(path);
        populate(db);
        return path;
    }

    private string EncryptFile(string plainPath)
    {
        var plainBytes = File.ReadAllBytes(plainPath);

        // Parse page size from SQLite header (bytes 16-17, big-endian)
        int pageSize = (plainBytes[16] << 8) | plainBytes[17];
        if (pageSize == 1) pageSize = 65536;
        int pageCount = plainBytes.Length / pageSize;

        // Derive key
        var salt = new byte[32];
        RandomNumberGenerator.Fill(salt);
        var passwordBytes = Encoding.UTF8.GetBytes(TestPassword);

        using var keyHandle = SharcKeyHandle.DeriveKey(passwordBytes, salt,
            timeCost: 1, memoryCostKiB: 64, parallelism: 1);

        var verificationHash = keyHandle.ComputeHmac(salt);

        // Encrypt pages
        using var transform = new AesGcmPageTransform(keyHandle);
        int encPageSize = transform.TransformedPageSize(pageSize);
        var encryptedFile = new byte[EncryptionHeader.HeaderSize + (encPageSize * pageCount)];

        EncryptionHeader.Write(encryptedFile,
            kdfAlgorithm: 1,
            cipherAlgorithm: 1,
            timeCost: 1,
            memoryCostKiB: 64,
            parallelism: 1,
            salt: salt,
            verificationHash: verificationHash,
            pageSize: pageSize,
            pageCount: pageCount);

        for (int i = 0; i < pageCount; i++)
        {
            uint pageNum = (uint)(i + 1);
            var plainPage = plainBytes.AsSpan(i * pageSize, pageSize);
            var encDest = encryptedFile.AsSpan(
                EncryptionHeader.HeaderSize + (i * encPageSize), encPageSize);
            transform.TransformWrite(plainPage, encDest, pageNum);
        }

        var encPath = Path.Combine(_tempDir, $"{Path.GetFileNameWithoutExtension(plainPath)}_enc.sharc");
        File.WriteAllBytes(encPath, encryptedFile);
        return encPath;
    }

    private static SharcDatabase OpenEncrypted(string path)
    {
        return SharcDatabase.Open(path, new SharcOpenOptions
        {
            Encryption = new SharcEncryptionOptions { Password = TestPassword }
        });
    }

    private static ColumnValue TextVal(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        return ColumnValue.Text(2 * bytes.Length + 13, bytes);
    }

    #endregion

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); }
        catch { /* cleanup best-effort */ }
        GC.SuppressFinalize(this);
    }
}
