// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.


/*-------------------------------------------------------------------------------------------------!
  "Where the mind is free to imagine and the craft is guided by clarity, code awakens."            |

  A collaborative work shaped by Artificial Intelligence and curated with intent by Ram Revanur.
  Licensed under the MIT License — free for personal and commercial use.                           |
--------------------------------------------------------------------------------------------------*/

using Sharc.Core;
using Sharc.IntegrationTests.Helpers;
using Xunit;

namespace Sharc.IntegrationTests;

public sealed class PreparedAgentTests : IDisposable
{
    private readonly string _dbPath;

    public PreparedAgentTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"prepared_agent_{Guid.NewGuid():N}.db");
    }

    public void Dispose()
    {
        try { File.Delete(_dbPath); }
        catch { /* best-effort cleanup */ }
    }

    private (SharcDatabase Db, SharcWriter Writer) CreateWritableDb(int seedRows = 0)
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(seedRows);
        File.WriteAllBytes(_dbPath, data);
        var db = SharcDatabase.Open(_dbPath, new SharcOpenOptions { Writable = true });
        var writer = SharcWriter.From(db);
        return (db, writer);
    }

    // ─── Builder Lifecycle ───────────────────────────────────────

    [Fact]
    public void PrepareAgent_ReturnsBuilder()
    {
        var (db, writer) = CreateWritableDb();
        using var _ = db;
        using var __ = writer;

        var builder = db.PrepareAgent();
        Assert.NotNull(builder);
    }

    [Fact]
    public void Build_EmptyBuilder_ReturnsAgent()
    {
        var (db, writer) = CreateWritableDb();
        using var _ = db;
        using var __ = writer;

        using var agent = db.PrepareAgent().Build();
        Assert.NotNull(agent);
    }

    [Fact]
    public void Dispose_DoesNotThrow()
    {
        var (db, writer) = CreateWritableDb();
        using var _ = db;
        using var __ = writer;

        var agent = db.PrepareAgent().Build();
        agent.Dispose();
    }

    [Fact]
    public void Dispose_Twice_DoesNotThrow()
    {
        var (db, writer) = CreateWritableDb();
        using var _ = db;
        using var __ = writer;

        var agent = db.PrepareAgent().Build();
        agent.Dispose();
        agent.Dispose();
    }

    // ─── Read Step ───────────────────────────────────────────────

    [Fact]
    public void ReadStep_ExecutesCallback()
    {
        var (db, writer) = CreateWritableDb(5);
        using var _ = db;
        using var __ = writer;
        using var reader = db.PrepareReader("users");

        string? capturedName = null;
        using var agent = db.PrepareAgent()
            .Read(reader, r =>
            {
                if (r.Seek(3))
                    capturedName = r.GetString(1);
            })
            .Build();

        agent.Execute();

        Assert.Equal("User3", capturedName);
    }

    [Fact]
    public void ReadStep_MultipleReads()
    {
        var (db, writer) = CreateWritableDb(5);
        using var _ = db;
        using var __ = writer;
        using var reader1 = db.PrepareReader("users");
        using var reader2 = db.PrepareReader("users");

        string? name1 = null, name2 = null;
        using var agent = db.PrepareAgent()
            .Read(reader1, r => { if (r.Seek(1)) name1 = r.GetString(1); })
            .Read(reader2, r => { if (r.Seek(5)) name2 = r.GetString(1); })
            .Build();

        agent.Execute();

        Assert.Equal("User1", name1);
        Assert.Equal("User5", name2);
    }

    // ─── Insert Step ─────────────────────────────────────────────

    [Fact]
    public void InsertStep_InsertsRow()
    {
        var (db, writer) = CreateWritableDb();
        using var _ = db;
        using var __ = writer;
        using var prepWriter = writer.PrepareWriter("users");

        using var agent = db.PrepareAgent()
            .Insert(prepWriter, () => new[]
            {
                ColumnValue.FromInt64(1, 1),
                ColumnValue.Text(25, System.Text.Encoding.UTF8.GetBytes("AgentUser")),
                ColumnValue.FromInt64(2, 30),
                ColumnValue.FromDouble(100.0),
                ColumnValue.Null()
            })
            .Build();

        var result = agent.Execute();

        Assert.Equal(1, result.StepsExecuted);
        Assert.Single(result.InsertedRowIds);
        Assert.True(result.InsertedRowIds[0] > 0);
    }

    // ─── Delete Step ─────────────────────────────────────────────

    [Fact]
    public void DeleteStep_RemovesRow()
    {
        var (db, writer) = CreateWritableDb(5);
        using var _ = db;
        using var __ = writer;
        using var prepWriter = writer.PrepareWriter("users");

        using var agent = db.PrepareAgent()
            .Delete(prepWriter, () => 3)
            .Build();

        var result = agent.Execute();

        Assert.Equal(1, result.StepsExecuted);
        Assert.Equal(1, result.RowsAffected);

        // Verify row is gone
        using var reader = db.CreateReader("users");
        int count = 0;
        while (reader.Read()) count++;
        Assert.Equal(4, count);
    }

    // ─── Update Step ─────────────────────────────────────────────

    [Fact]
    public void UpdateStep_ModifiesRow()
    {
        var (db, writer) = CreateWritableDb(5);
        using var _ = db;
        using var __ = writer;
        using var prepWriter = writer.PrepareWriter("users");

        using var agent = db.PrepareAgent()
            .Update(prepWriter, () => 2, () => new[]
            {
                ColumnValue.FromInt64(1, 2),
                ColumnValue.Text(25, System.Text.Encoding.UTF8.GetBytes("Updated")),
                ColumnValue.FromInt64(2, 99),
                ColumnValue.FromDouble(999.0),
                ColumnValue.Null()
            })
            .Build();

        var result = agent.Execute();

        Assert.Equal(1, result.StepsExecuted);
        Assert.Equal(1, result.RowsAffected);

        using var reader = db.CreateReader("users", "name");
        Assert.True(reader.Seek(2));
        Assert.Equal("Updated", reader.GetString(0));
    }

    // ─── Composite (Read + Write) ────────────────────────────────

    [Fact]
    public void Composite_ReadThenInsert()
    {
        var (db, writer) = CreateWritableDb(5);
        using var _ = db;
        using var __ = writer;
        using var prepReader = db.PrepareReader("users");
        using var prepWriter = writer.PrepareWriter("users");

        string? sourceName = null;
        using var agent = db.PrepareAgent()
            .Read(prepReader, r =>
            {
                if (r.Seek(3))
                    sourceName = r.GetString(1);
            })
            .Insert(prepWriter, () => new[]
            {
                ColumnValue.FromInt64(1, 100),
                ColumnValue.Text(25, System.Text.Encoding.UTF8.GetBytes($"Copy_{sourceName}")),
                ColumnValue.FromInt64(2, 40),
                ColumnValue.FromDouble(400.0),
                ColumnValue.Null()
            })
            .Build();

        var result = agent.Execute();

        Assert.Equal(2, result.StepsExecuted);
        Assert.Equal("User3", sourceName);
        Assert.Single(result.InsertedRowIds);
    }

    // ─── Reuse ───────────────────────────────────────────────────

    [Fact]
    public void Execute_MultipleTimes_Reusable()
    {
        var (db, writer) = CreateWritableDb(5);
        using var _ = db;
        using var __ = writer;
        using var prepReader = db.PrepareReader("users");

        string? captured = null;
        using var agent = db.PrepareAgent()
            .Read(prepReader, r =>
            {
                if (r.Seek(1))
                    captured = r.GetString(1);
            })
            .Build();

        agent.Execute();
        Assert.Equal("User1", captured);

        captured = null;
        agent.Execute();
        Assert.Equal("User1", captured); // same result on second execute
    }

    // ─── Result ──────────────────────────────────────────────────

    [Fact]
    public void Result_EmptyAgent_ZeroSteps()
    {
        var (db, writer) = CreateWritableDb();
        using var _ = db;
        using var __ = writer;

        using var agent = db.PrepareAgent().Build();
        var result = agent.Execute();

        Assert.Equal(0, result.StepsExecuted);
        Assert.Empty(result.InsertedRowIds);
        Assert.Equal(0, result.RowsAffected);
    }

    // ─── Disposed ────────────────────────────────────────────────

    [Fact]
    public void Execute_AfterDispose_ThrowsObjectDisposedException()
    {
        var (db, writer) = CreateWritableDb();
        using var _ = db;
        using var __ = writer;

        var agent = db.PrepareAgent().Build();
        agent.Dispose();

        Assert.Throws<ObjectDisposedException>(() => agent.Execute());
    }
}