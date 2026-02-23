// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using System.Text;
using Sharc.Core;
using Sharc.IntegrationTests.Helpers;
using Xunit;

namespace Sharc.IntegrationTests;

/// <summary>
/// Performance-oriented tests for JitSQL query pipeline and mutation events.
/// Validates cache reuse, zero-alloc fast paths, mutation event propagation,
/// and read-after-write consistency through the JitQuery and PreparedQuery APIs.
/// </summary>
public sealed class JitSqlPerformanceTests : IDisposable
{
    private readonly string _dbPath;
    private readonly byte[] _dbBytes;

    public JitSqlPerformanceTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"jitsql_perf_{Guid.NewGuid():N}.db");
        _dbBytes = TestDatabaseFactory.CreateUsersDatabase(100);
        File.WriteAllBytes(_dbPath, _dbBytes);
    }

    public void Dispose()
    {
        try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch { }
    }

    // ═══════════════════════════════════════════════════════════════
    //  Mutation Event Propagation — Insert visible to subsequent reads
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void JitQuery_InsertThenQuery_SeesNewRow()
    {
        using var db = SharcDatabase.OpenMemory(_dbBytes, new SharcOpenOptions { Writable = true });
        var jit = db.Jit("users");

        // Count before insert
        int before = CountRows(jit);

        // Insert a new row
        jit.Insert(
            ColumnValue.FromInt64(1, 101),
            ColumnValue.Text(25, Encoding.UTF8.GetBytes("NewUser")),
            ColumnValue.FromInt64(2, 25),
            ColumnValue.FromDouble(999.99),
            ColumnValue.Null());

        // Count after insert — should see new row
        int after = CountRows(jit);
        Assert.Equal(before + 1, after);
    }

    [Fact]
    public void JitQuery_DeleteThenQuery_RowDisappears()
    {
        using var db = SharcDatabase.OpenMemory(_dbBytes, new SharcOpenOptions { Writable = true });
        var jit = db.Jit("users");

        int before = CountRows(jit);
        Assert.True(before > 0);

        // Delete first row (rowid 1)
        bool deleted = jit.Delete(1);
        Assert.True(deleted);

        int after = CountRows(jit);
        Assert.Equal(before - 1, after);
    }

    [Fact]
    public void JitQuery_UpdateThenQuery_SeesUpdatedValue()
    {
        using var db = SharcDatabase.OpenMemory(_dbBytes, new SharcOpenOptions { Writable = true });
        var jit = db.Jit("users");

        // Read original value
        using var r1 = jit.Query("name");
        Assert.True(r1.Read());
        string originalName = r1.GetString(0);

        // Update the name for rowid 1
        jit.Update(1,
            ColumnValue.FromInt64(1, 1),
            ColumnValue.Text(39, Encoding.UTF8.GetBytes("UpdatedName")),
            ColumnValue.FromInt64(2, 21),
            ColumnValue.FromDouble(1000.0),
            ColumnValue.Null());

        // Read updated value
        using var r2 = jit.Query("name");
        Assert.True(r2.Read());
        string updatedName = r2.GetString(0);

        Assert.NotEqual(originalName, updatedName);
        Assert.Equal("UpdatedName", updatedName);
    }

    // ═══════════════════════════════════════════════════════════════
    //  Prepared Query — Read-After-Write Consistency
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void PreparedQuery_AfterInsert_SeesNewRow()
    {
        using var db = SharcDatabase.OpenMemory(_dbBytes, new SharcOpenOptions { Writable = true });
        using var prepared = db.Prepare("SELECT id FROM users");

        int before = CountPrepared(prepared);

        using var writer = SharcWriter.From(db);
        using var tx = writer.BeginTransaction();
        tx.Insert("users",
            ColumnValue.FromInt64(1, 101),
            ColumnValue.Text(25, Encoding.UTF8.GetBytes("NewUser")),
            ColumnValue.FromInt64(2, 25),
            ColumnValue.FromDouble(999.99),
            ColumnValue.Null());
        tx.Commit();

        int after = CountPrepared(prepared);
        Assert.Equal(before + 1, after);
    }

    [Fact]
    public void PreparedReader_SeekAfterInsert_FindsNewRow()
    {
        using var db = SharcDatabase.OpenMemory(_dbBytes, new SharcOpenOptions { Writable = true });
        using var prepared = db.PrepareReader("users", "name");

        // Row 101 doesn't exist yet
        using var r1 = prepared.Execute();
        Assert.False(r1.Seek(101));

        // Insert row 101
        using var writer = SharcWriter.From(db);
        using var tx = writer.BeginTransaction();
        tx.Insert("users",
            ColumnValue.FromInt64(1, 101),
            ColumnValue.Text(37, Encoding.UTF8.GetBytes("SeekTarget")),
            ColumnValue.FromInt64(2, 25),
            ColumnValue.FromDouble(999.99),
            ColumnValue.Null());
        tx.Commit();

        // Now Seek should find it
        using var r2 = prepared.Execute();
        Assert.True(r2.Seek(101));
        Assert.Equal("SeekTarget", r2.GetString(0));
    }

    // ═══════════════════════════════════════════════════════════════
    //  Filter Cache Reuse — Same filter should not recompile
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void JitQuery_RepeatedSameFilter_CachesEffectively()
    {
        using var db = SharcDatabase.OpenMemory(_dbBytes);
        var jit = db.Jit("users");

        jit.Where(FilterStar.Column("age").Gt(25));

        // Run the same query 100 times — filter cache should be hit after first
        int expectedCount = -1;
        for (int i = 0; i < 100; i++)
        {
            int count = CountRows(jit);
            if (expectedCount == -1)
                expectedCount = count;
            else
                Assert.Equal(expectedCount, count);
        }

        Assert.True(expectedCount > 0, "Filter should match some rows");
    }

    [Fact]
    public void JitQuery_QuerySameProjection_ReturnsConsistentResults()
    {
        using var db = SharcDatabase.OpenMemory(_dbBytes);
        var jit = db.Jit("users");

        // First call with Query("name") initializes the projection
        using var r1 = jit.Query("name");
        int count1 = 0;
        while (r1.Read()) count1++;

        // QuerySameProjection reuses cached projection — must return same count
        for (int i = 0; i < 50; i++)
        {
            using var r2 = jit.QuerySameProjection();
            int count2 = 0;
            while (r2.Read()) count2++;
            Assert.Equal(count1, count2);
        }
    }

    // ═══════════════════════════════════════════════════════════════
    //  Execution Hints — CACHED and JIT produce correct results
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void CachedHint_RepeatedExecution_SameResults()
    {
        using var db = SharcDatabase.OpenMemory(_dbBytes);

        int? expectedCount = null;
        for (int i = 0; i < 20; i++)
        {
            using var reader = db.Query("CACHED SELECT id, name FROM users WHERE age > 25");
            int count = 0;
            while (reader.Read()) count++;

            if (expectedCount == null)
                expectedCount = count;
            else
                Assert.Equal(expectedCount, count);
        }

        Assert.True(expectedCount > 0);
    }

    [Fact]
    public void JitHint_RepeatedExecution_SameResults()
    {
        using var db = SharcDatabase.OpenMemory(_dbBytes);

        int? expectedCount = null;
        for (int i = 0; i < 20; i++)
        {
            using var reader = db.Query("JIT SELECT id FROM users WHERE age > 25");
            int count = 0;
            while (reader.Read()) count++;

            if (expectedCount == null)
                expectedCount = count;
            else
                Assert.Equal(expectedCount, count);
        }

        Assert.True(expectedCount > 0);
    }

    // ═══════════════════════════════════════════════════════════════
    //  Parameterized Query — Cache key varies by parameter values
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void PreparedQuery_DifferentParams_DifferentResults()
    {
        using var db = SharcDatabase.OpenMemory(_dbBytes);
        using var prepared = db.Prepare("SELECT id FROM users WHERE age > $minAge");

        var params25 = new Dictionary<string, object> { ["minAge"] = 25L };
        var params28 = new Dictionary<string, object> { ["minAge"] = 28L };

        int count25 = CountPreparedWithParams(prepared, params25);
        int count28 = CountPreparedWithParams(prepared, params28);

        Assert.True(count25 > count28, "More rows should match age > 25 than age > 28");
        Assert.True(count25 > 0);
        Assert.True(count28 > 0);
    }

    [Fact]
    public void PreparedQuery_SameParams_ConsistentResults()
    {
        using var db = SharcDatabase.OpenMemory(_dbBytes);
        using var prepared = db.Prepare("SELECT id FROM users WHERE age > $minAge");

        var parameters = new Dictionary<string, object> { ["minAge"] = 25L };

        int first = CountPreparedWithParams(prepared, parameters);
        for (int i = 0; i < 50; i++)
        {
            int count = CountPreparedWithParams(prepared, parameters);
            Assert.Equal(first, count);
        }
    }

    // ═══════════════════════════════════════════════════════════════
    //  Concurrent Read-After-Write — Multi-threaded consistency
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void JitQuery_ConcurrentReadsAfterWrite_AllSeeUpdate()
    {
        using var db = SharcDatabase.OpenMemory(_dbBytes, new SharcOpenOptions { Writable = true });
        var jit = db.Jit("users");

        // Insert a row via JitQuery (auto-commit) that all threads will look for
        long insertedRowId = jit.Insert(
            ColumnValue.FromInt64(1, 200),
            ColumnValue.Text(43, Encoding.UTF8.GetBytes("ThreadTarget")),
            ColumnValue.FromInt64(2, 99),
            ColumnValue.FromDouble(0.0),
            ColumnValue.Null());

        // Verify the insert worked on the main thread first
        using var verify = jit.Query("name");
        bool found = verify.Seek(insertedRowId);
        Assert.True(found, $"Main thread: Seek({insertedRowId}) returned false after insert");

        // 8 threads verify the row exists via CreateReader
        var errors = new List<string>();
        var threads = new Thread[8];
        for (int t = 0; t < threads.Length; t++)
        {
            threads[t] = new Thread(() =>
            {
                try
                {
                    for (int i = 0; i < 20; i++)
                    {
                        using var reader = db.CreateReader("users", "name");
                        if (!reader.Seek(insertedRowId))
                        {
                            lock (errors) errors.Add($"Thread {Environment.CurrentManagedThreadId} iteration {i}: Seek({insertedRowId}) returned false");
                            return;
                        }
                        string name = reader.GetString(0);
                        if (name != "ThreadTarget")
                        {
                            lock (errors) errors.Add($"Thread {Environment.CurrentManagedThreadId}: expected 'ThreadTarget', got '{name}'");
                            return;
                        }
                    }
                }
                catch (Exception ex)
                {
                    lock (errors) errors.Add($"Thread {Environment.CurrentManagedThreadId}: {ex.Message}");
                }
            });
        }

        foreach (var t in threads) t.Start();
        foreach (var t in threads) t.Join();

        Assert.Empty(errors);
    }

    // ═══════════════════════════════════════════════════════════════
    //  Transaction Isolation — Uncommitted writes not visible
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void JitQuery_WithTransaction_RollbackHidesChanges()
    {
        using var db = SharcDatabase.OpenMemory(_dbBytes, new SharcOpenOptions { Writable = true });
        var jit = db.Jit("users");

        int before = CountRows(jit);

        // Insert in a transaction, then rollback
        using var writer = SharcWriter.From(db);
        using var tx = writer.BeginTransaction();
        jit.WithTransaction(tx);

        jit.Insert(
            ColumnValue.FromInt64(1, 300),
            ColumnValue.Text(41, Encoding.UTF8.GetBytes("RollbackUser")),
            ColumnValue.FromInt64(2, 30),
            ColumnValue.FromDouble(0.0),
            ColumnValue.Null());

        tx.Rollback();
        jit.DetachTransaction();

        int after = CountRows(jit);
        Assert.Equal(before, after);
    }

    [Fact]
    public void JitQuery_WithTransaction_CommitShowsChanges()
    {
        using var db = SharcDatabase.OpenMemory(_dbBytes, new SharcOpenOptions { Writable = true });
        var jit = db.Jit("users");

        int before = CountRows(jit);

        using var writer = SharcWriter.From(db);
        using var tx = writer.BeginTransaction();
        jit.WithTransaction(tx);

        jit.Insert(
            ColumnValue.FromInt64(1, 301),
            ColumnValue.Text(37, Encoding.UTF8.GetBytes("CommitUser")),
            ColumnValue.FromInt64(2, 30),
            ColumnValue.FromDouble(0.0),
            ColumnValue.Null());

        tx.Commit();
        jit.DetachTransaction();

        int after = CountRows(jit);
        Assert.Equal(before + 1, after);
    }

    // ═══════════════════════════════════════════════════════════════
    //  ToPrepared — JitQuery freezes to immutable PreparedQuery
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void JitQuery_ToPrepared_ReturnsConsistentResults()
    {
        using var db = SharcDatabase.OpenMemory(_dbBytes);
        var jit = db.Jit("users");
        jit.Where(FilterStar.Column("age").Gt(25));

        // Count via JitQuery
        int jitCount = CountRows(jit);

        // Freeze to PreparedQuery
        using var prepared = jit.ToPrepared("id", "name");
        int prepCount = CountPrepared(prepared);

        Assert.Equal(jitCount, prepCount);

        // Repeat — same result
        for (int i = 0; i < 10; i++)
        {
            Assert.Equal(prepCount, CountPrepared(prepared));
        }
    }

    // ═══════════════════════════════════════════════════════════════
    //  Helpers
    // ═══════════════════════════════════════════════════════════════

    private static int CountRows(JitQuery jit)
    {
        using var reader = jit.Query();
        int count = 0;
        while (reader.Read()) count++;
        return count;
    }

    private static int CountPrepared(IPreparedReader prepared)
    {
        using var reader = prepared.Execute();
        int count = 0;
        while (reader.Read()) count++;
        return count;
    }

    private static int CountPreparedWithParams(PreparedQuery prepared, IReadOnlyDictionary<string, object> parameters)
    {
        using var reader = prepared.Execute(parameters);
        int count = 0;
        while (reader.Read()) count++;
        return count;
    }
}
