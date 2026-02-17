using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using Sharc.Crypto;
using Sharc.Exceptions;
using Xunit;

namespace Sharc.IntegrationTests;

/// <summary>
/// Integration tests for Sharc-encrypted database files.
/// Creates real SQLite databases, encrypts them at the page level,
/// and verifies SharcDatabase.Open can read them with the correct password.
/// </summary>
public class EncryptionIntegrationTests : IDisposable
{
    private readonly string _tempDir;
    private const string TestPassword = "correct-horse-battery-staple";

    public EncryptionIntegrationTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"sharc_enc_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    [Fact]
    public void Open_EncryptedDatabase_ReadsData()
    {
        var encPath = CreateEncryptedDatabase("enc_read", conn =>
        {
            Execute(conn, "CREATE TABLE items (id INTEGER PRIMARY KEY, name TEXT, value REAL)");
            Execute(conn, "INSERT INTO items VALUES (1, 'Alpha', 1.5)");
            Execute(conn, "INSERT INTO items VALUES (2, 'Beta', 2.5)");
            Execute(conn, "INSERT INTO items VALUES (3, 'Gamma', 3.5)");
        });

        using var db = SharcDatabase.Open(encPath, new SharcOpenOptions
        {
            Encryption = new SharcEncryptionOptions { Password = TestPassword }
        });

        using var reader = db.CreateReader("items");
        var rows = new List<(long id, string name, double value)>();
        while (reader.Read())
        {
            rows.Add((reader.GetInt64(0), reader.GetString(1), reader.GetDouble(2)));
        }

        Assert.Equal(3, rows.Count);
        Assert.Equal("Alpha", rows[0].name);
        Assert.Equal("Beta", rows[1].name);
        Assert.Equal("Gamma", rows[2].name);
        Assert.Equal(1.5, rows[0].value);
    }

    [Fact]
    public void Open_EncryptedDatabase_WrongPassword_Throws()
    {
        var encPath = CreateEncryptedDatabase("enc_wrong_pw", conn =>
        {
            Execute(conn, "CREATE TABLE t (id INTEGER PRIMARY KEY)");
        });

        var ex = Assert.Throws<SharcCryptoException>(() =>
            SharcDatabase.Open(encPath, new SharcOpenOptions
            {
                Encryption = new SharcEncryptionOptions { Password = "wrong-password" }
            }));

        Assert.Contains("Wrong password", ex.Message);
    }

    [Fact]
    public void Open_EncryptedDatabase_NoPassword_Throws()
    {
        var encPath = CreateEncryptedDatabase("enc_no_pw", conn =>
        {
            Execute(conn, "CREATE TABLE t (id INTEGER PRIMARY KEY)");
        });

        var ex = Assert.Throws<SharcCryptoException>(() =>
            SharcDatabase.Open(encPath));

        Assert.Contains("Password required", ex.Message);
    }

    [Fact]
    public void Info_EncryptedDatabase_IsEncryptedTrue()
    {
        var encPath = CreateEncryptedDatabase("enc_info", conn =>
        {
            Execute(conn, "CREATE TABLE data (id INTEGER PRIMARY KEY, payload BLOB)");
            Execute(conn, "INSERT INTO data VALUES (1, X'DEADBEEF')");
        });

        using var db = SharcDatabase.Open(encPath, new SharcOpenOptions
        {
            Encryption = new SharcEncryptionOptions { Password = TestPassword }
        });

        Assert.True(db.Info.IsEncrypted);
    }

    [Fact]
    public void Open_EncryptedDatabase_MultipleTablesAndSchema()
    {
        var encPath = CreateEncryptedDatabase("enc_schema", conn =>
        {
            Execute(conn, "CREATE TABLE users (id INTEGER PRIMARY KEY, name TEXT)");
            Execute(conn, "CREATE TABLE posts (id INTEGER PRIMARY KEY, user_id INTEGER, title TEXT)");
            Execute(conn, "INSERT INTO users VALUES (1, 'Alice')");
            Execute(conn, "INSERT INTO posts VALUES (1, 1, 'Hello World')");
        });

        using var db = SharcDatabase.Open(encPath, new SharcOpenOptions
        {
            Encryption = new SharcEncryptionOptions { Password = TestPassword }
        });

        Assert.Equal(3, db.Schema.Tables.Count); // users, posts, sqlite_master

        using var userReader = db.CreateReader("users");
        Assert.True(userReader.Read());
        Assert.Equal("Alice", userReader.GetString(1));

        using var postReader = db.CreateReader("posts");
        Assert.True(postReader.Read());
        Assert.Equal("Hello World", postReader.GetString(2));
    }

    #region Helpers

    /// <summary>
    /// Creates a plain SQLite DB, then encrypts it page-by-page into a Sharc-encrypted file.
    /// </summary>
    private string CreateEncryptedDatabase(string name, Action<SqliteConnection> setup)
    {
        // Step 1: Create a plain SQLite DB in delete journal mode (no WAL)
        var plainPath = Path.Combine(_tempDir, $"{name}_plain.db");
        var connStr = $"Data Source={plainPath}";

        using (var conn = new SqliteConnection(connStr))
        {
            conn.Open();
            Execute(conn, "PRAGMA journal_mode=DELETE");
            setup(conn);
        }

        // Clear the connection pool to release file locks on Windows
        SqliteConnection.ClearAllPools();

        // Step 2: Read plain DB bytes
        var plainBytes = File.ReadAllBytes(plainPath);

        // Step 3: Determine page size from the SQLite header (bytes 16-17, big-endian)
        int pageSize = (plainBytes[16] << 8) | plainBytes[17];
        if (pageSize == 1) pageSize = 65536; // SQLite convention
        int pageCount = plainBytes.Length / pageSize;

        // Step 4: Derive key
        var salt = new byte[32];
        RandomNumberGenerator.Fill(salt);
        var passwordBytes = Encoding.UTF8.GetBytes(TestPassword);

        using var keyHandle = SharcKeyHandle.DeriveKey(passwordBytes, salt,
            timeCost: 1, memoryCostKiB: 64, parallelism: 1);

        // Step 5: Compute verification hash
        var verificationHash = keyHandle.ComputeHmac(salt);

        // Step 6: Encrypt each page
        using var transform = new AesGcmPageTransform(keyHandle);
        int encPageSize = transform.TransformedPageSize(pageSize);

        var encryptedFile = new byte[EncryptionHeader.HeaderSize + (encPageSize * pageCount)];

        // Write the 128-byte header
        EncryptionHeader.Write(encryptedFile,
            kdfAlgorithm: 1, // Argon2id/PBKDF2
            cipherAlgorithm: 1, // AES-256-GCM
            timeCost: 1,
            memoryCostKiB: 64,
            parallelism: 1,
            salt: salt,
            verificationHash: verificationHash,
            pageSize: pageSize,
            pageCount: pageCount);

        // Encrypt pages
        for (int i = 0; i < pageCount; i++)
        {
            uint pageNum = (uint)(i + 1);
            var plainPage = plainBytes.AsSpan(i * pageSize, pageSize);
            var encDest = encryptedFile.AsSpan(
                EncryptionHeader.HeaderSize + (i * encPageSize), encPageSize);
            transform.TransformWrite(plainPage, encDest, pageNum);
        }

        // Step 7: Write encrypted file
        var encPath = Path.Combine(_tempDir, $"{name}.sharc");
        File.WriteAllBytes(encPath, encryptedFile);

        return encPath;
    }

    private static void Execute(SqliteConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    #endregion

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); }
        catch { /* cleanup best-effort */ }
        GC.SuppressFinalize(this);
    }
}
