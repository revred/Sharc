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

using Sharc;
using Sharc.Codec;
using Sharc.Core;
using Xunit;

namespace Sharc.Tests.Codec;

public class SharcCborIntegrationTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SharcDatabase _db;

    public SharcCborIntegrationTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"sharc_cbor_{Guid.NewGuid()}.db");
        _db = SharcDatabase.Create(_dbPath);
    }

    public void Dispose()
    {
        _db?.Dispose();
        if (File.Exists(_dbPath))
            File.Delete(_dbPath);
        if (File.Exists(_dbPath + ".journal"))
            File.Delete(_dbPath + ".journal");
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void WriterInsert_CborBlob_ReaderDecodes()
    {
        // Create table with a BLOB column for CBOR payloads
        using (var tx = _db.BeginTransaction())
        {
            tx.Execute("CREATE TABLE Events (Id INTEGER PRIMARY KEY, Data BLOB)");
            tx.Commit();
        }

        // Prepare CBOR payload
        var payload = new Dictionary<string, object?>
        {
            ["action"] = "file_edit",
            ["tokens"] = 512L,
            ["confidence"] = 0.95,
            ["approved"] = true,
            ["meta"] = new Dictionary<string, object?> { ["source"] = "agent-7" }
        };

        // Insert via SharcCbor.ToColumnValue()
        using (var writer = SharcWriter.From(_db))
        {
            writer.Insert("Events",
                ColumnValue.FromInt64(1, 1),
                SharcCbor.ToColumnValue(payload));
        }

        // Read back via SharcDataReader extension methods
        using var reader = _db.CreateReader("Events");
        Assert.True(reader.Read());
        Assert.Equal(1L, reader.GetInt64(0));

        // Full decode via extension method
        var decoded = reader.GetCborMap(1);
        Assert.Equal("file_edit", decoded["action"]);
        Assert.Equal(512L, decoded["tokens"]);
        Assert.Equal(0.95, (double)decoded["confidence"]!, 10);
        Assert.Equal(true, decoded["approved"]);
        var meta = Assert.IsType<Dictionary<string, object?>>(decoded["meta"]);
        Assert.Equal("agent-7", meta["source"]);
    }

    [Fact]
    public void WriterInsert_CborBlob_SelectiveFieldExtraction()
    {
        using (var tx = _db.BeginTransaction())
        {
            tx.Execute("CREATE TABLE Logs (Id INTEGER PRIMARY KEY, Payload BLOB)");
            tx.Commit();
        }

        var payload = new Dictionary<string, object?>
        {
            ["level"] = "info",
            ["message"] = "Task completed",
            ["duration_ms"] = 42L,
            ["success"] = true
        };

        using (var writer = SharcWriter.From(_db))
        {
            writer.Insert("Logs",
                ColumnValue.FromInt64(1, 1),
                SharcCbor.ToColumnValue(payload));
        }

        // Selective extraction — only read "duration_ms" without decoding the full map
        using var reader = _db.CreateReader("Logs");
        Assert.True(reader.Read());

        var duration = reader.GetCborField<long>(1, "duration_ms");
        Assert.Equal(42L, duration);

        var level = reader.GetCborField<string>(1, "level");
        Assert.Equal("info", level);

        // Missing key returns default
        var missing = reader.GetCborField<string>(1, "nonexistent");
        Assert.Null(missing);
    }

    [Fact]
    public void WriterInsert_MultipleCborRows_AllRoundTrip()
    {
        using (var tx = _db.BeginTransaction())
        {
            tx.Execute("CREATE TABLE Items (Id INTEGER PRIMARY KEY, Props BLOB)");
            tx.Commit();
        }

        // Insert 10 rows with different CBOR payloads
        using (var writer = SharcWriter.From(_db))
        {
            for (int i = 1; i <= 10; i++)
            {
                var props = new Dictionary<string, object?>
                {
                    ["index"] = (long)i,
                    ["label"] = $"item_{i}",
                    ["active"] = i % 2 == 0
                };
                writer.Insert("Items",
                    ColumnValue.FromInt64(1, i),
                    SharcCbor.ToColumnValue(props));
            }
        }

        // Read all back
        using var reader = _db.CreateReader("Items");
        int count = 0;
        while (reader.Read())
        {
            count++;
            long id = reader.GetInt64(0);
            var props = reader.GetCborMap(1);
            Assert.Equal(id, (long)props["index"]!);
            Assert.Equal($"item_{id}", props["label"]);
            Assert.Equal(id % 2 == 0, props["active"]);
        }
        Assert.Equal(10, count);
    }

    [Fact]
    public void SharcCbor_Encode_SmallerThanJsonText()
    {
        // Compare CBOR BLOB vs JSON TEXT for a representative payload
        var data = new Dictionary<string, object?>
        {
            ["agent_id"] = "agent-alpha-7",
            ["action"] = "code_review",
            ["timestamp"] = 1708800000L,
            ["confidence"] = 0.87,
            ["approved"] = false,
            ["details"] = (string?)null,
            ["signature"] = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08 }
        };

        byte[] cborBytes = SharcCbor.Encode(data);
        string json = System.Text.Json.JsonSerializer.Serialize(data);
        byte[] jsonBytes = System.Text.Encoding.UTF8.GetBytes(json);

        // CBOR should be meaningfully smaller
        Assert.True(cborBytes.Length < jsonBytes.Length,
            $"CBOR ({cborBytes.Length} bytes) should be smaller than JSON ({jsonBytes.Length} bytes)");
    }

    [Fact]
    public void SharcCbor_PublicApi_RoundTrips()
    {
        // Test the public SharcCbor API directly (no database)
        var input = new Dictionary<string, object?>
        {
            ["key"] = "value",
            ["count"] = 99L
        };

        byte[] cbor = SharcCbor.Encode(input);
        var decoded = SharcCbor.Decode(cbor);

        Assert.Equal("value", decoded["key"]);
        Assert.Equal(99L, decoded["count"]);

        // Selective extraction
        Assert.Equal(99L, SharcCbor.ReadField(cbor, "count"));
        Assert.Equal("value", SharcCbor.ReadField<string>(cbor, "key"));
        Assert.Null(SharcCbor.ReadField(cbor, "missing"));
    }
}
