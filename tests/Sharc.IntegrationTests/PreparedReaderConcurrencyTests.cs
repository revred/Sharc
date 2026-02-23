/*-------------------------------------------------------------------------------------------------!
  "Where the mind is free to imagine and the craft is guided by clarity, code awakens."            |

  A collaborative work shaped by Artificial Intelligence and curated with intent by Ram Revanur.
  Licensed under the MIT License — free for personal and commercial use.                           |
--------------------------------------------------------------------------------------------------*/

using Sharc.IntegrationTests.Helpers;
using Xunit;

namespace Sharc.IntegrationTests;

/// <summary>
/// Tests that <see cref="PreparedReader"/>, <see cref="PreparedQuery"/>, and
/// <see cref="PreparedWriter"/> are thread-safe: a single instance shared across
/// N threads produces correct, isolated results on each thread.
/// </summary>
public sealed class PreparedReaderConcurrencyTests
{
    private const int ThreadCount = 8;
    private const int IterationsPerThread = 50;

    // ─── PreparedReader: Concurrent Seek ────────────────────────────

    [Fact]
    public void PreparedReader_ConcurrentSeek_AllThreadsGetCorrectData()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(100);
        using var db = SharcDatabase.OpenMemory(data);
        using var prepared = db.PrepareReader("users");

        var errors = new System.Collections.Concurrent.ConcurrentBag<string>();

        Parallel.For(0, ThreadCount, threadId =>
        {
            for (int i = 0; i < IterationsPerThread; i++)
            {
                long targetId = (threadId * 10 + i) % 100 + 1; // 1..100
                using var reader = prepared.CreateReader();

                if (!reader.Seek(targetId))
                {
                    errors.Add($"Thread {threadId}: Seek({targetId}) returned false");
                    continue;
                }

                var name = reader.GetString(1);
                var expectedName = $"User{targetId}";
                if (name != expectedName)
                    errors.Add($"Thread {threadId}: Seek({targetId}) got name='{name}', expected='{expectedName}'");
            }
        });

        Assert.Empty(errors);
    }

    [Fact]
    public void PreparedReader_ConcurrentFullScan_AllThreadsGetAllRows()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(50);
        using var db = SharcDatabase.OpenMemory(data);
        using var prepared = db.PrepareReader("users");

        var errors = new System.Collections.Concurrent.ConcurrentBag<string>();

        Parallel.For(0, ThreadCount, threadId =>
        {
            for (int i = 0; i < IterationsPerThread; i++)
            {
                using var reader = prepared.CreateReader();
                int count = 0;
                while (reader.Read()) count++;

                if (count != 50)
                    errors.Add($"Thread {threadId}, iter {i}: got {count} rows, expected 50");
            }
        });

        Assert.Empty(errors);
    }

    // ─── PreparedReader with Projection ─────────────────────────────

    [Fact]
    public void PreparedReader_ConcurrentProjection_IsolatedFieldCounts()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(20);
        using var db = SharcDatabase.OpenMemory(data);
        using var prepared = db.PrepareReader("users", "name", "age");

        var errors = new System.Collections.Concurrent.ConcurrentBag<string>();

        Parallel.For(0, ThreadCount, threadId =>
        {
            for (int i = 0; i < IterationsPerThread; i++)
            {
                using var reader = prepared.CreateReader();

                if (reader.FieldCount != 2)
                {
                    errors.Add($"Thread {threadId}: FieldCount={reader.FieldCount}, expected 2");
                    continue;
                }

                if (!reader.Seek(5))
                {
                    errors.Add($"Thread {threadId}: Seek(5) returned false");
                    continue;
                }

                var name = reader.GetString(0);
                var age = reader.GetInt32(1);
                if (name != "User5")
                    errors.Add($"Thread {threadId}: name='{name}', expected 'User5'");
                if (age != 25)
                    errors.Add($"Thread {threadId}: age={age}, expected 25");
            }
        });

        Assert.Empty(errors);
    }

    // ─── PreparedQuery: Concurrent Execute ──────────────────────────

    [Fact]
    public void PreparedQuery_ConcurrentExecute_AllThreadsGetCorrectResults()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(100);
        using var db = SharcDatabase.OpenMemory(data);
        using var prepared = db.Prepare("SELECT id, name FROM users WHERE age > 50");

        var errors = new System.Collections.Concurrent.ConcurrentBag<string>();

        Parallel.For(0, ThreadCount, threadId =>
        {
            for (int i = 0; i < IterationsPerThread; i++)
            {
                using var reader = prepared.Execute();
                int count = 0;
                while (reader.Read())
                {
                    _ = reader.GetInt64(0);
                    _ = reader.GetString(1);
                    count++;
                }

                // age = 20 + id, so age > 50 means id > 30 → 70 rows (31..100)
                if (count != 70)
                    errors.Add($"Thread {threadId}, iter {i}: got {count} rows, expected 70");
            }
        });

        Assert.Empty(errors);
    }

    // ─── PreparedReader: Dispose while threads active ───────────────

    [Fact]
    public void PreparedReader_DisposeAfterAllThreadsDone_NoExceptions()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(20);
        using var db = SharcDatabase.OpenMemory(data);
        var prepared = db.PrepareReader("users");

        Parallel.For(0, ThreadCount, _ =>
        {
            for (int i = 0; i < IterationsPerThread; i++)
            {
                using var reader = prepared.CreateReader();
                reader.Seek(1);
            }
        });

        // Dispose after all threads complete — should clean up all per-thread slots
        prepared.Dispose();
    }

    // ─── Mixed: PreparedReader + PreparedQuery on same table ────────

    [Fact]
    public void Mixed_PreparedReaderAndQuery_ConcurrentOnSameTable()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(50);
        using var db = SharcDatabase.OpenMemory(data);
        using var preparedReader = db.PrepareReader("users");
        using var preparedQuery = db.Prepare("SELECT name FROM users WHERE age > 30");

        var errors = new System.Collections.Concurrent.ConcurrentBag<string>();

        Parallel.For(0, ThreadCount, threadId =>
        {
            for (int i = 0; i < IterationsPerThread; i++)
            {
                if (threadId % 2 == 0)
                {
                    // Even threads: PreparedReader
                    using var reader = preparedReader.CreateReader();
                    Assert.True(reader.Seek(1));
                }
                else
                {
                    // Odd threads: PreparedQuery
                    using var reader = preparedQuery.Execute();
                    int count = 0;
                    while (reader.Read()) count++;

                    // age = 20 + id, so age > 30 means id > 10 → 40 rows (11..50)
                    if (count != 40)
                        errors.Add($"Thread {threadId}, iter {i}: query got {count} rows, expected 40");
                }
            }
        });

        Assert.Empty(errors);
    }
}
