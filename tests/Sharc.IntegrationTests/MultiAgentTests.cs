// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Core;
using Sharc.Core.IO;
using Sharc.IntegrationTests.Helpers;
using Sharc.Trust;
using Xunit;

namespace Sharc.IntegrationTests;

/// <summary>
/// Integration tests for multi-agent DataVersion / IsStale coordination.
/// Uses in-memory databases (MemoryPageSource) which track DataVersion.
/// File-backed databases (FilePageSource) return DataVersion=0 by design,
/// so IsStale is always false for file-backed sources.
/// </summary>
public class MultiAgentTests : IDisposable
{
    private readonly string _dbPath;

    public MultiAgentTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"multi_agent_{Guid.NewGuid()}.db");
    }

    [Fact]
    public void TwoAgents_Write_CursorDetectsStale()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(0);
        using var db = SharcDatabase.OpenMemory(data);

        // Agent B opens a reader (cursor) — currently no rows
        using var reader = db.CreateReader("users");
        Assert.False(reader.IsStale);

        // Agent A inserts a row
        using (var writer = SharcWriter.From(db))
        {
            writer.Insert("users",
                ColumnValue.FromInt64(1, 1),
                ColumnValue.Text(25, System.Text.Encoding.UTF8.GetBytes("Alice")),
                ColumnValue.FromInt64(2, 30),
                ColumnValue.FromDouble(100.0),
                ColumnValue.Null());
        }

        // Agent B's reader should now detect staleness
        Assert.True(reader.IsStale);
    }

    [Fact]
    public void TwoAgents_ResetRefreshesData()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(0);
        using var db = SharcDatabase.OpenMemory(data);

        // Agent B opens a reader
        using var reader = db.CreateReader("users");
        int countBefore = 0;
        while (reader.Read()) countBefore++;
        Assert.Equal(0, countBefore);

        // Agent A inserts 3 rows
        using (var writer = SharcWriter.From(db))
        {
            for (int i = 1; i <= 3; i++)
            {
                writer.Insert("users",
                    ColumnValue.FromInt64(1, i),
                    ColumnValue.Text(25, System.Text.Encoding.UTF8.GetBytes($"User{i}")),
                    ColumnValue.FromInt64(2, 20 + i),
                    ColumnValue.FromDouble(100.0 + i),
                    ColumnValue.Null());
            }
        }

        Assert.True(reader.IsStale);

        // Agent B creates a fresh reader to see updated data
        using var reader2 = db.CreateReader("users");
        int countAfter = 0;
        while (reader2.Read()) countAfter++;
        Assert.Equal(3, countAfter);
        Assert.False(reader2.IsStale);
    }

    [Fact]
    public void DataVersion_SurvivesCommit()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(0);
        using var db = SharcDatabase.OpenMemory(data);

        // Get DataVersion before write (via a reader)
        using var reader1 = db.CreateReader("users");
        Assert.False(reader1.IsStale);

        // Commit a transaction
        using (var writer = SharcWriter.From(db))
        {
            writer.Insert("users",
                ColumnValue.FromInt64(1, 1),
                ColumnValue.Text(25, System.Text.Encoding.UTF8.GetBytes("Alice")),
                ColumnValue.FromInt64(2, 30),
                ColumnValue.FromDouble(100.0),
                ColumnValue.Null());
        }

        // Reader from before commit should be stale
        Assert.True(reader1.IsStale);

        // New reader should NOT be stale
        using var reader2 = db.CreateReader("users");
        Assert.False(reader2.IsStale);
    }

    [Fact]
    public void ChessGame_TurnBasedWrites()
    {
        // Simulates a chess game: agents alternate writing moves and detecting changes
        var data = TestDatabaseFactory.CreateDatabaseWith(conn =>
        {
            TestDatabaseFactory.Execute(conn, @"
                CREATE TABLE chess_moves (
                    id INTEGER PRIMARY KEY,
                    agent TEXT NOT NULL,
                    move TEXT NOT NULL,
                    turn INTEGER NOT NULL
                )");
        });
        using var db = SharcDatabase.OpenMemory(data);

        // Agent A writes move 1
        using (var writer = SharcWriter.From(db))
        {
            writer.Insert("chess_moves",
                ColumnValue.FromInt64(1, 1),
                ColumnValue.Text(25, System.Text.Encoding.UTF8.GetBytes("AgentA")),
                ColumnValue.Text(25, System.Text.Encoding.UTF8.GetBytes("e2e4")),
                ColumnValue.FromInt64(1, 1));
        }

        // Agent B opens a reader and reads the move
        using var readerB = db.CreateReader("chess_moves", "agent", "move", "turn");
        Assert.True(readerB.Read());
        Assert.Equal("AgentA", readerB.GetString(0));
        Assert.Equal("e2e4", readerB.GetString(1));
        Assert.False(readerB.IsStale);

        // Agent B writes counter-move
        using (var writer = SharcWriter.From(db))
        {
            writer.Insert("chess_moves",
                ColumnValue.FromInt64(1, 2),
                ColumnValue.Text(25, System.Text.Encoding.UTF8.GetBytes("AgentB")),
                ColumnValue.Text(25, System.Text.Encoding.UTF8.GetBytes("e7e5")),
                ColumnValue.FromInt64(1, 2));
        }

        // Agent B's reader should now be stale (its own write committed)
        Assert.True(readerB.IsStale);

        // Agent A opens fresh reader, sees both moves
        using var readerA = db.CreateReader("chess_moves", "agent", "move");
        Assert.False(readerA.IsStale);

        Assert.True(readerA.Read());
        Assert.Equal("AgentA", readerA.GetString(0));
        Assert.True(readerA.Read());
        Assert.Equal("AgentB", readerA.GetString(0));
        Assert.False(readerA.Read());
    }

    [Fact]
    public void DataVersion_MemoryPageSource_IncrementsOnWrite()
    {
        // Direct test at the page source level to confirm version tracking
        var data = TestDatabaseFactory.CreateUsersDatabase(5);
        using var source = new MemoryPageSource(data);

        long v1 = source.DataVersion;
        Assert.True(v1 >= 1);

        source.WritePage(1, new byte[source.PageSize]);
        long v2 = source.DataVersion;
        Assert.True(v2 > v1);

        source.WritePage(2, new byte[source.PageSize]);
        long v3 = source.DataVersion;
        Assert.True(v3 > v2);
    }

    [Fact]
    public void ParallelReaders_Writer_StalenessDetected()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(10);
        using var db = SharcDatabase.OpenMemory(data);

        const int readerCount = 4;
        var readers = new SharcDataReader[readerCount];
        for (int i = 0; i < readerCount; i++)
            readers[i] = db.CreateReader("users");

        // All readers should start not stale
        foreach (var r in readers)
            Assert.False(r.IsStale);

        // Writer commits
        using (var writer = SharcWriter.From(db))
        {
            writer.Insert("users",
                ColumnValue.FromInt64(1, 99),
                ColumnValue.Text(25, System.Text.Encoding.UTF8.GetBytes("NewUser")),
                ColumnValue.FromInt64(2, 50),
                ColumnValue.FromDouble(999.0),
                ColumnValue.Null());
        }

        // All pre-existing readers should now be stale
        foreach (var r in readers)
        {
            Assert.True(r.IsStale);
            r.Dispose();
        }
    }

    [Fact]
    public void TwoAgents_LedgerChronology()
    {
        // Two agents alternate writes with ledger entries; VerifyIntegrity passes;
        // entries alternate agents
        var data = TestDatabaseFactory.CreateDatabaseWith(conn =>
        {
            TestDatabaseFactory.Execute(conn, @"
                CREATE TABLE _sharc_ledger (
                    SequenceNumber INTEGER PRIMARY KEY,
                    Timestamp INTEGER,
                    AgentId TEXT,
                    Payload BLOB,
                    PayloadHash BLOB,
                    PreviousHash BLOB,
                    Signature BLOB
                )");
            TestDatabaseFactory.Execute(conn, @"
                CREATE TABLE _sharc_agents (
                    AgentId TEXT PRIMARY KEY,
                    Class INTEGER,
                    PublicKey BLOB,
                    AuthorityCeiling INTEGER,
                    WriteScope TEXT,
                    ReadScope TEXT,
                    ValidityStart INTEGER,
                    ValidityEnd INTEGER,
                    ParentAgent TEXT,
                    CoSignRequired INTEGER,
                    Signature BLOB
                )");
            TestDatabaseFactory.Execute(conn, @"
                CREATE TABLE _sharc_scores (
                    AgentId TEXT PRIMARY KEY,
                    Score REAL,
                    Confidence REAL,
                    LastUpdated INTEGER,
                    LastRatingCount INTEGER
                )");
        });
        File.WriteAllBytes(_dbPath, data);

        using var db = SharcDatabase.Open(_dbPath, new SharcOpenOptions { Writable = true });

        var ledger = new LedgerManager(db);
        var registry = new AgentRegistry(db);

        using var signerA = new SharcSigner("AgentA");
        using var signerB = new SharcSigner("AgentB");

        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        long future = DateTimeOffset.UtcNow.AddYears(1).ToUnixTimeSeconds();

        // Register both agents
        registry.RegisterAgent(CreateAgent(signerA, now, future));
        registry.RegisterAgent(CreateAgent(signerB, now, future));

        // Alternate writes
        ledger.Append("AgentA: e2e4 (turn 1)", signerA);
        ledger.Append("AgentB: e7e5 (turn 2)", signerB);
        ledger.Append("AgentA: Nf3 (turn 3)", signerA);
        ledger.Append("AgentB: Nc6 (turn 4)", signerB);

        // Verify integrity of the full chain
        Assert.True(ledger.VerifyIntegrity());

        // Verify alternating agent pattern via deltas
        var deltas = ledger.ExportDeltas(1);
        Assert.Equal(4, deltas.Count);

        // Decode agent IDs from ledger entries
        var agentIds = new List<string>();
        foreach (var delta in deltas)
        {
            var decoded = db.RecordDecoder.DecodeRecord(delta);
            agentIds.Add(decoded[2].AsString()); // AgentId is column 2
        }

        Assert.Equal("AgentA", agentIds[0]);
        Assert.Equal("AgentB", agentIds[1]);
        Assert.Equal("AgentA", agentIds[2]);
        Assert.Equal("AgentB", agentIds[3]);
    }

    [Fact]
    public void CachedLeafPage_AgentWriteInvalidatesCache_FreshReaderSeesUpdatedData()
    {
        // Full multi-agent flow with cached leaf page optimization:
        // 1. Agent B opens reader (cursor caches leaf page internally)
        // 2. Agent A writes new rows (DataVersion increments)
        // 3. Agent B detects staleness via IsStale
        // 4. Agent B creates a new reader — sees updated data (fresh cache)
        var data = TestDatabaseFactory.CreateUsersDatabase(5);
        using var db = SharcDatabase.OpenMemory(data);

        // Agent B reads current data — cursor caches the leaf pages
        using var readerB = db.CreateReader("users", "id");
        int countBefore = 0;
        while (readerB.Read())
        {
            _ = readerB.GetInt64(0);
            countBefore++;
        }
        Assert.Equal(5, countBefore);
        Assert.False(readerB.IsStale);

        // Agent A inserts 3 new rows — DataVersion changes
        using (var writer = SharcWriter.From(db))
        {
            for (int i = 100; i <= 102; i++)
            {
                writer.Insert("users",
                    ColumnValue.FromInt64(1, i),
                    ColumnValue.Text(25, System.Text.Encoding.UTF8.GetBytes($"Agent{i}")),
                    ColumnValue.FromInt64(2, 25),
                    ColumnValue.FromDouble(50.0),
                    ColumnValue.Null());
            }
        }

        // Agent B's cached reader is now stale
        Assert.True(readerB.IsStale);

        // Agent B opens fresh reader — new cursor, fresh leaf page cache
        using var readerB2 = db.CreateReader("users", "id");
        Assert.False(readerB2.IsStale);
        int countAfter = 0;
        while (readerB2.Read())
        {
            _ = readerB2.GetInt64(0);
            countAfter++;
        }
        Assert.Equal(8, countAfter);
        Assert.False(readerB2.IsStale);
    }

    [Fact]
    public void CachedLeafPage_MultipleReaders_WriterCommit_AllDetectStale()
    {
        // Multiple agent readers with cached leaf pages detect staleness
        // when a writer agent commits.
        var data = TestDatabaseFactory.CreateUsersDatabase(10);
        using var db = SharcDatabase.OpenMemory(data);

        // 3 agent readers, each caching leaf pages via MoveNext()
        var readers = new SharcDataReader[3];
        for (int i = 0; i < 3; i++)
        {
            readers[i] = db.CreateReader("users", "id");
            while (readers[i].Read()) { _ = readers[i].GetInt64(0); }
        }

        // All readers should start fresh
        foreach (var r in readers)
            Assert.False(r.IsStale);

        // Writer commits
        using (var writer = SharcWriter.From(db))
        {
            writer.Insert("users",
                ColumnValue.FromInt64(1, 999),
                ColumnValue.Text(25, System.Text.Encoding.UTF8.GetBytes("Writer")),
                ColumnValue.FromInt64(2, 40),
                ColumnValue.FromDouble(100.0),
                ColumnValue.Null());
        }

        // All cached readers detect staleness
        foreach (var r in readers)
        {
            Assert.True(r.IsStale);
            r.Dispose();
        }
    }

    public void Dispose()
    {
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
        GC.SuppressFinalize(this);
    }

    private static Sharc.Core.Trust.AgentInfo CreateAgent(ISharcSigner signer, long start, long end)
    {
        var pub = signer.GetPublicKey();
        var wScope = "*";
        var rScope = "*";
        var parent = "";
        var cosign = false;

        int bufferSize = System.Text.Encoding.UTF8.GetByteCount(signer.AgentId) + 1 + pub.Length + 8 +
                         System.Text.Encoding.UTF8.GetByteCount(wScope) + System.Text.Encoding.UTF8.GetByteCount(rScope) +
                         8 + 8 + System.Text.Encoding.UTF8.GetByteCount(parent) + 1;

        byte[] data = new byte[bufferSize];
        int offset = 0;
        offset += System.Text.Encoding.UTF8.GetBytes(signer.AgentId, data.AsSpan(offset));
        data[offset++] = (byte)Sharc.Core.Trust.AgentClass.User;
        pub.CopyTo(data.AsSpan(offset));
        offset += pub.Length;
        System.Buffers.Binary.BinaryPrimitives.WriteUInt64BigEndian(data.AsSpan(offset), ulong.MaxValue);
        offset += 8;
        offset += System.Text.Encoding.UTF8.GetBytes(wScope, data.AsSpan(offset));
        offset += System.Text.Encoding.UTF8.GetBytes(rScope, data.AsSpan(offset));
        System.Buffers.Binary.BinaryPrimitives.WriteInt64BigEndian(data.AsSpan(offset), start);
        offset += 8;
        System.Buffers.Binary.BinaryPrimitives.WriteInt64BigEndian(data.AsSpan(offset), end);
        offset += 8;
        offset += System.Text.Encoding.UTF8.GetBytes(parent, data.AsSpan(offset));
        data[offset++] = cosign ? (byte)1 : (byte)0;

        var sig = signer.Sign(data);

        return new Sharc.Core.Trust.AgentInfo(signer.AgentId, Sharc.Core.Trust.AgentClass.User, pub, ulong.MaxValue,
            wScope, rScope, start, end, parent, cosign, sig);
    }
}
