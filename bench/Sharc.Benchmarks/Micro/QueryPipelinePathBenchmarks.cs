// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using BenchmarkDotNet.Attributes;
using Microsoft.Data.Sqlite;

namespace Sharc.Benchmarks.Micro;

[BenchmarkCategory("Micro", "QueryPipeline", "TopN")]
[MemoryDiagnoser]
public class TopNPathBenchmarks
{
    private SharcDatabase _db = null!;

    private const string TopNSingleKeyQuery = """
        SELECT id, score, tie_break, payload_text, payload_blob, payload_note
        FROM topn_source
        ORDER BY score DESC
        LIMIT 256 OFFSET 64
        """;

    private const string TopNTwoKeyQuery = """
        SELECT id, score, tie_break, payload_text, payload_blob, payload_note
        FROM topn_source
        ORDER BY score DESC, tie_break DESC
        LIMIT 256 OFFSET 64
        """;

    [GlobalSetup]
    public void Setup()
    {
        _db = SharcDatabase.OpenMemory(QueryPipelineBenchmarkData.GetBytes());
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _db.Dispose();
    }

    [Benchmark(Baseline = true)]
    public long TopN_TwoKey_MaterializedControl()
    {
        return CountRows(TopNTwoKeyQuery);
    }

    [Benchmark]
    public long TopN_SingleKey_DeferredPath()
    {
        return CountRows(TopNSingleKeyQuery);
    }

    private long CountRows(string sql)
    {
        long count = 0;
        using var reader = _db.Query(sql);
        while (reader.Read())
            count++;
        return count;
    }
}

[BenchmarkCategory("Micro", "QueryPipeline", "SetOpFingerprint")]
[MemoryDiagnoser]
public class SetOpFingerprintBenchmarks
{
    private SharcDatabase _db = null!;

    private const string NumericFastPathQuery = """
        SELECT metric FROM set_left
        UNION
        SELECT metric FROM set_right
        """;

    private const string TextFallbackQuery = """
        SELECT label FROM set_left
        UNION
        SELECT label FROM set_right
        """;

    [GlobalSetup]
    public void Setup()
    {
        _db = SharcDatabase.OpenMemory(QueryPipelineBenchmarkData.GetBytes());
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _db.Dispose();
    }

    [Benchmark(Baseline = true)]
    public long SetOp_TextFallback()
    {
        return CountRows(TextFallbackQuery);
    }

    [Benchmark]
    public long SetOp_NumericFastPath()
    {
        return CountRows(NumericFastPathQuery);
    }

    private long CountRows(string sql)
    {
        long count = 0;
        using var reader = _db.Query(sql);
        while (reader.Read())
            count++;
        return count;
    }
}

internal static class QueryPipelineBenchmarkData
{
    private static readonly Lazy<byte[]> CachedBytes = new(BuildBytes);

    internal static byte[] GetBytes() => CachedBytes.Value;

    private static byte[] BuildBytes()
    {
        string dir = Path.Combine(Path.GetTempPath(), "sharc_bench");
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, "query_pipeline_paths.db");
        if (File.Exists(path))
            File.Delete(path);

        using (var conn = new SqliteConnection($"Data Source={path};Pooling=false"))
        {
            conn.Open();

            using (var pragma = conn.CreateCommand())
            {
                pragma.CommandText = """
                    PRAGMA journal_mode=DELETE;
                    PRAGMA page_size=4096;
                    PRAGMA synchronous=OFF;
                    PRAGMA temp_store=MEMORY;
                    """;
                pragma.ExecuteNonQuery();
            }

            using (var schema = conn.CreateCommand())
            {
                schema.CommandText = """
                    CREATE TABLE topn_source (
                        id           INTEGER PRIMARY KEY,
                        score        INTEGER NOT NULL,
                        tie_break    INTEGER NOT NULL,
                        payload_text TEXT NOT NULL,
                        payload_blob BLOB NOT NULL,
                        payload_note TEXT
                    );

                    CREATE TABLE set_left (
                        metric INTEGER NOT NULL,
                        label  TEXT NOT NULL
                    );

                    CREATE TABLE set_right (
                        metric INTEGER NOT NULL,
                        label  TEXT NOT NULL
                    );
                    """;
                schema.ExecuteNonQuery();
            }

            var rng = new Random(1337);

            using (var tx = conn.BeginTransaction())
            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = """
                    INSERT INTO topn_source (score, tie_break, payload_text, payload_blob, payload_note)
                    VALUES (@score, @tie, @txt, @blob, @note)
                    """;

                var score = cmd.Parameters.Add("@score", SqliteType.Integer);
                var tie = cmd.Parameters.Add("@tie", SqliteType.Integer);
                var text = cmd.Parameters.Add("@txt", SqliteType.Text);
                var blob = cmd.Parameters.Add("@blob", SqliteType.Blob);
                var note = cmd.Parameters.Add("@note", SqliteType.Text);

                for (int i = 0; i < 25_000; i++)
                {
                    score.Value = rng.Next(0, 2_000_000);
                    tie.Value = rng.Next(0, 2_000_000);
                    text.Value = $"payload_{i:D6}_{new string((char)('a' + (i % 26)), 48)}";

                    var bytes = new byte[64];
                    rng.NextBytes(bytes);
                    blob.Value = bytes;

                    note.Value = i % 4 == 0 ? DBNull.Value : $"note_{i:D6}";
                    cmd.ExecuteNonQuery();
                }

                tx.Commit();
            }

            using (var tx = conn.BeginTransaction())
            using (var left = conn.CreateCommand())
            using (var right = conn.CreateCommand())
            {
                left.Transaction = tx;
                right.Transaction = tx;
                left.CommandText = "INSERT INTO set_left (metric, label) VALUES (@m, @l)";
                right.CommandText = "INSERT INTO set_right (metric, label) VALUES (@m, @l)";

                var lMetric = left.Parameters.Add("@m", SqliteType.Integer);
                var lLabel = left.Parameters.Add("@l", SqliteType.Text);
                for (int metric = 0; metric < 40_000; metric++)
                {
                    lMetric.Value = metric;
                    lLabel.Value = $"m_{metric:D6}";
                    left.ExecuteNonQuery();
                }

                var rMetric = right.Parameters.Add("@m", SqliteType.Integer);
                var rLabel = right.Parameters.Add("@l", SqliteType.Text);
                for (int metric = 20_000; metric < 60_000; metric++)
                {
                    rMetric.Value = metric;
                    rLabel.Value = $"m_{metric:D6}";
                    right.ExecuteNonQuery();
                }

                tx.Commit();
            }
        }

        return File.ReadAllBytes(path);
    }
}
