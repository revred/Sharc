/*-------------------------------------------------------------------------------------------------!
  "Where the mind is free to imagine and the craft is guided by clarity, code awakens."            |

  A collaborative work shaped by Artificial Intelligence and curated with intent by Ram Revanur.
  Software here is treated not as static text, but as a living system designed to learn and evolve.
  Built on the belief that architecture and context often define outcomes before code is written.

  This file reflects an AI-aware, agentic, context-driven, and continuously evolving approach
  to modern engineering. If you seek to transform a traditional codebase into an adaptive,
  intelligence-guided system, you may find resonance in these patterns and principles.

  Subtle conversations often begin with a single message â€” or a prompt with the right context.
  https://www.linkedin.com/in/revodoc/

  Licensed under the MIT License â€” free for personal and commercial use.                           |
--------------------------------------------------------------------------------------------------*/

using Sharc.IntegrationTests.Helpers;
using Xunit;

namespace Sharc.IntegrationTests;

/// <summary>
/// Validates that Sharc handles concurrent and parallel access correctly.
/// Multiple readers on the same database, same tables, same rows, and same fields
/// must return consistent, correct data without corruption or exceptions.
/// </summary>
public class ConcurrencyTests
{
    // â”€â”€â”€ Same Table, Parallel Readers â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [Fact]
    public void ParallelReaders_SameTable_AllGetCorrectRowCount()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(100);
        using var db = SharcDatabase.OpenMemory(data);

        const int threadCount = 8;
        var rowCounts = new long[threadCount];
        var exceptions = new Exception?[threadCount];

        Parallel.For(0, threadCount, i =>
        {
            try
            {
                using var reader = db.CreateReader("users");
                long count = 0;
                while (reader.Read())
                    count++;
                rowCounts[i] = count;
            }
            catch (Exception ex)
            {
                exceptions[i] = ex;
            }
        });

        for (int i = 0; i < threadCount; i++)
        {
            Assert.Null(exceptions[i]);
            Assert.Equal(100, rowCounts[i]);
        }
    }

    [Fact]
    public void ParallelReaders_SameTable_AllGetCorrectFieldValues()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(50);
        using var db = SharcDatabase.OpenMemory(data);

        const int threadCount = 8;
        var results = new List<(long id, string name, long age)>[threadCount];
        var exceptions = new Exception?[threadCount];

        Parallel.For(0, threadCount, i =>
        {
            try
            {
                var rows = new List<(long id, string name, long age)>();
                using var reader = db.CreateReader("users");
                while (reader.Read())
                {
                    long id = reader.GetInt64(0);
                    string name = reader.GetString(1);
                    long age = reader.GetInt64(2);
                    rows.Add((id, name, age));
                }
                results[i] = rows;
            }
            catch (Exception ex)
            {
                exceptions[i] = ex;
            }
        });

        for (int i = 0; i < threadCount; i++)
        {
            Assert.Null(exceptions[i]);
            Assert.Equal(50, results[i].Count);

            // Verify every thread got identical data
            for (int row = 0; row < 50; row++)
            {
                var r = results[i][row];
                Assert.Equal(row + 1, r.id);
                Assert.Equal($"User{row + 1}", r.name);
                Assert.Equal(20 + row + 1, r.age);
            }
        }
    }

    [Fact]
    public void ParallelReaders_SameTable_16Threads_NoExceptions()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(200);
        using var db = SharcDatabase.OpenMemory(data);

        const int threadCount = 16;
        var exceptions = new Exception?[threadCount];

        Parallel.For(0, threadCount, new ParallelOptions { MaxDegreeOfParallelism = threadCount }, i =>
        {
            try
            {
                using var reader = db.CreateReader("users");
                while (reader.Read())
                {
                    _ = reader.GetInt64(0);
                    _ = reader.GetString(1);
                    _ = reader.GetInt64(2);
                    _ = reader.GetDouble(3);
                    _ = reader.GetBlob(4);
                }
            }
            catch (Exception ex)
            {
                exceptions[i] = ex;
            }
        });

        Assert.All(exceptions, ex => Assert.Null(ex));
    }

    // â”€â”€â”€ Different Tables, Parallel Readers â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [Fact]
    public void ParallelReaders_DifferentTables_AllGetCorrectData()
    {
        var data = TestDatabaseFactory.CreateMultiTableDatabase();
        using var db = SharcDatabase.OpenMemory(data);

        var tableNames = db.Schema.Tables.Select(t => t.Name).ToArray();
        var rowCounts = new Dictionary<string, long>();
        var exceptions = new List<Exception>();
        var lockObj = new object();

        Parallel.ForEach(tableNames, tableName =>
        {
            try
            {
                using var reader = db.CreateReader(tableName);
                long count = 0;
                while (reader.Read())
                    count++;

                lock (lockObj)
                    rowCounts[tableName] = count;
            }
            catch (Exception ex)
            {
                lock (lockObj)
                    exceptions.Add(ex);
            }
        });

        Assert.Empty(exceptions);
        Assert.Equal(tableNames.Length, rowCounts.Count);
        Assert.All(rowCounts.Values, count => Assert.True(count > 0));
    }

    // â”€â”€â”€ Large Table, Parallel Scans â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [Fact]
    public void ParallelReaders_LargeTable_AllGetSameChecksum()
    {
        var data = TestDatabaseFactory.CreateLargeDatabase(2000);
        using var db = SharcDatabase.OpenMemory(data);

        const int threadCount = 8;
        var checksums = new long[threadCount];
        var rowCounts = new long[threadCount];
        var exceptions = new Exception?[threadCount];

        Parallel.For(0, threadCount, i =>
        {
            try
            {
                long checksum = 0;
                long count = 0;
                using var reader = db.CreateReader("large_table");
                while (reader.Read())
                {
                    long id = reader.GetInt64(0);
                    string value = reader.GetString(1);
                    long number = reader.GetInt64(2);
                    checksum += id + number + value.Length;
                    count++;
                }
                checksums[i] = checksum;
                rowCounts[i] = count;
            }
            catch (Exception ex)
            {
                exceptions[i] = ex;
            }
        });

        Assert.All(exceptions, ex => Assert.Null(ex));
        Assert.All(rowCounts, count => Assert.Equal(2000, count));

        // All threads must compute the same checksum
        long expected = checksums[0];
        Assert.All(checksums, cs => Assert.Equal(expected, cs));
    }

    // â”€â”€â”€ Mixed Operations: Readers + Schema + GetRowCount â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [Fact]
    public void ParallelMixedOps_ReadersSchemaAndRowCount_AllStable()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(100);
        using var db = SharcDatabase.OpenMemory(data);

        const int iterations = 50;
        var exceptions = new List<Exception>();
        var lockObj = new object();

        Parallel.For(0, iterations, i =>
        {
            try
            {
                switch (i % 3)
                {
                    case 0:
                        // Read schema
                        var tables = db.Schema.Tables;
                        Assert.Equal(2, tables.Count); // users + sqlite_master
                        Assert.Contains(tables, t => t.Name == "users");
                        Assert.Contains(tables, t => t.Name == "sqlite_master");
                        break;
                    case 1:
                        // GetRowCount
                        long count = db.GetRowCount("users");
                        Assert.Equal(100, count);
                        break;
                    case 2:
                        // Full table scan
                        using (var reader = db.CreateReader("users"))
                        {
                            int rows = 0;
                            while (reader.Read())
                            {
                                _ = reader.GetInt64(0);
                                _ = reader.GetString(1);
                                rows++;
                            }
                            Assert.Equal(100, rows);
                        }
                        break;
                }
            }
            catch (Exception ex)
            {
                lock (lockObj)
                    exceptions.Add(ex);
            }
        });

        Assert.Empty(exceptions);
    }

    // â”€â”€â”€ Sustained Parallel Load â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [Fact]
    public void SustainedParallelLoad_100Iterations_NoCorruption()
    {
        var data = TestDatabaseFactory.CreateAllTypesDatabase();
        using var db = SharcDatabase.OpenMemory(data);

        const int totalIterations = 100;
        var exceptions = new List<Exception>();
        var lockObj = new object();

        Parallel.For(0, totalIterations, new ParallelOptions { MaxDegreeOfParallelism = 8 }, i =>
        {
            try
            {
                using var reader = db.CreateReader("all_types");
                int rowIndex = 0;
                while (reader.Read())
                {
                    // Access every column in every row
                    var type0 = reader.GetColumnType(0);
                    var type1 = reader.GetColumnType(1);
                    var type2 = reader.GetColumnType(2);
                    var type3 = reader.GetColumnType(3);
                    var type4 = reader.GetColumnType(4);
                    var type5 = reader.GetColumnType(5);

                    _ = reader.GetInt64(0); // id

                    if (!reader.IsNull(1))
                        _ = reader.GetInt64(1);
                    if (!reader.IsNull(2))
                        _ = reader.GetDouble(2);
                    if (!reader.IsNull(3))
                        _ = reader.GetString(3);
                    if (!reader.IsNull(4))
                        _ = reader.GetBlob(4);

                    rowIndex++;
                }
                Assert.Equal(5, rowIndex);
            }
            catch (Exception ex)
            {
                lock (lockObj)
                    exceptions.Add(ex);
            }
        });

        Assert.Empty(exceptions);
    }

    // â”€â”€â”€ Column Projection, Parallel â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [Fact]
    public void ParallelReaders_WithColumnProjection_CorrectSubset()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(50);
        using var db = SharcDatabase.OpenMemory(data);

        const int threadCount = 8;
        var exceptions = new Exception?[threadCount];
        var names = new List<string>[threadCount];

        Parallel.For(0, threadCount, i =>
        {
            try
            {
                var collected = new List<string>();
                using var reader = db.CreateReader("users", "name", "age");
                Assert.Equal(2, reader.FieldCount);

                while (reader.Read())
                    collected.Add(reader.GetString(0));

                names[i] = collected;
            }
            catch (Exception ex)
            {
                exceptions[i] = ex;
            }
        });

        Assert.All(exceptions, ex => Assert.Null(ex));
        Assert.All(names, list =>
        {
            Assert.Equal(50, list.Count);
            for (int j = 0; j < 50; j++)
                Assert.Equal($"User{j + 1}", list[j]);
        });
    }

    // â”€â”€â”€ Same Table Different Projections in Parallel â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [Fact]
    public void ParallelReaders_DifferentProjections_SameTable_NoConflict()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(30);
        using var db = SharcDatabase.OpenMemory(data);

        var exceptions = new List<Exception>();
        var lockObj = new object();

        var projections = new[]
        {
            new[] { "id", "name" },
            new[] { "name", "age" },
            new[] { "balance" },
            new[] { "id", "name", "age", "balance", "avatar" }
        };

        Parallel.For(0, 32, i =>
        {
            try
            {
                var cols = projections[i % projections.Length];
                using var reader = db.CreateReader("users", cols);
                Assert.Equal(cols.Length, reader.FieldCount);

                int count = 0;
                while (reader.Read())
                    count++;
                Assert.Equal(30, count);
            }
            catch (Exception ex)
            {
                lock (lockObj)
                    exceptions.Add(ex);
            }
        });

        Assert.Empty(exceptions);
    }

    // â”€â”€â”€ File-Backed Concurrent Access â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [Fact]
    public void ParallelReaders_FileBacked_AllGetCorrectData()
    {
        var dbBytes = TestDatabaseFactory.CreateUsersDatabase(100);
        var tempPath = Path.Combine(Path.GetTempPath(), $"sharc_concurrent_{Guid.NewGuid():N}.db");
        try
        {
            File.WriteAllBytes(tempPath, dbBytes);
            using var db = SharcDatabase.Open(tempPath);

            const int threadCount = 8;
            var rowCounts = new long[threadCount];
            var exceptions = new Exception?[threadCount];

            Parallel.For(0, threadCount, i =>
            {
                try
                {
                    using var reader = db.CreateReader("users");
                    long count = 0;
                    while (reader.Read())
                    {
                        _ = reader.GetInt64(0);
                        _ = reader.GetString(1);
                        count++;
                    }
                    rowCounts[i] = count;
                }
                catch (Exception ex)
                {
                    exceptions[i] = ex;
                }
            });

            Assert.All(exceptions, ex => Assert.Null(ex));
            Assert.All(rowCounts, count => Assert.Equal(100, count));
        }
        finally
        {
            try { File.Delete(tempPath); }
            catch { /* best-effort cleanup */ }
        }
    }

    // â”€â”€â”€ Rapid Create/Dispose Readers â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [Fact]
    public void RapidCreateDispose_ManyReaders_NoResourceLeak()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(10);
        using var db = SharcDatabase.OpenMemory(data);

        var exceptions = new List<Exception>();
        var lockObj = new object();

        Parallel.For(0, 200, new ParallelOptions { MaxDegreeOfParallelism = 16 }, _ =>
        {
            try
            {
                using var reader = db.CreateReader("users");
                reader.Read();
                _ = reader.GetInt64(0);
                // Immediately dispose â€” tests cleanup under contention
            }
            catch (Exception ex)
            {
                lock (lockObj)
                    exceptions.Add(ex);
            }
        });

        Assert.Empty(exceptions);
    }

    // â”€â”€â”€ Interleaved Row Access Across Threads â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [Fact]
    public void ParallelReaders_InterleavedAccess_DataNeverCrossTalks()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(100);
        using var db = SharcDatabase.OpenMemory(data);

        const int threadCount = 8;
        var allRowIds = new HashSet<long>[threadCount];
        var exceptions = new Exception?[threadCount];
        using var barrier = new Barrier(threadCount);

        var threads = new Thread[threadCount];
        for (int t = 0; t < threadCount; t++)
        {
            int threadIndex = t;
            threads[t] = new Thread(() =>
            {
                try
                {
                    var ids = new HashSet<long>();
                    using var reader = db.CreateReader("users");

                    // Synchronize start so all threads read simultaneously
                    barrier.SignalAndWait();

                    while (reader.Read())
                    {
                        long id = reader.GetInt64(0);
                        string name = reader.GetString(1);

                        // Verify data integrity â€” name must match id
                        Assert.Equal($"User{id}", name);
                        ids.Add(id);
                    }
                    allRowIds[threadIndex] = ids;
                }
                catch (Exception ex)
                {
                    exceptions[threadIndex] = ex;
                }
            });
            threads[t].Start();
        }

        foreach (var thread in threads)
            thread.Join();

        Assert.All(exceptions, ex => Assert.Null(ex));

        // Every thread must see all 100 rows
        Assert.All(allRowIds, ids =>
        {
            Assert.Equal(100, ids.Count);
            for (long i = 1; i <= 100; i++)
                Assert.Contains(i, ids);
        });
    }
}
