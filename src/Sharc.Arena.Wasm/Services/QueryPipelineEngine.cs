// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using System.Diagnostics;
using System.Net.Http.Json;
using Microsoft.Data.Sqlite;
using Sharc.Arena.Wasm.Models;

namespace Sharc.Arena.Wasm.Services;

/// <summary>
/// Runs 13 query comparisons live: Sharc.Query() vs SqliteCommand.
/// Falls back to static JSON if engines are not initialized.
/// </summary>
public sealed class QueryPipelineEngine
{
    private readonly HttpClient _http;
    private readonly SharcEngine _sharcEngine;
    private readonly SqliteEngine _sqliteEngine;

    private IReadOnlyList<QueryResult>? _fallbackResults;

    /// <summary>True if the last call to RunLiveAsync executed live queries (not fallback JSON).</summary>
    public bool IsLive { get; private set; }

    public QueryPipelineEngine(HttpClient http, SharcEngine sharcEngine, SqliteEngine sqliteEngine)
    {
        _http = http;
        _sharcEngine = sharcEngine;
        _sqliteEngine = sqliteEngine;
    }

    /// <summary>
    /// Runs all 13 queries live against both engines.
    /// Returns fallback JSON results if engines are not initialized.
    /// </summary>
    public async Task<IReadOnlyList<QueryResult>> RunLiveAsync()
    {
        var db = _sharcEngine.Database;
        var conn = _sqliteEngine.Connection;

        if (db is null || conn is null)
        {
            IsLive = false;
            return await LoadFallbackAsync();
        }

        var results = new List<QueryResult>(Queries.Length);
        foreach (var spec in Queries)
        {
            results.Add(RunSingleQuery(db, conn, spec));
        }
        IsLive = true;
        return results;
    }

    /// <summary>
    /// Loads pre-computed results from JSON as fallback.
    /// </summary>
    public async Task<IReadOnlyList<QueryResult>> LoadFallbackAsync()
    {
        _fallbackResults ??= await _http.GetFromJsonAsync("data/query-benchmarks.json", ArenaJsonContext.Default.ListQueryResult) ?? [];
        return _fallbackResults;
    }

    private const int WarmupIterations = 3;
    private const int MeasuredIterations = 5;

    private static QueryResult RunSingleQuery(SharcDatabase db, SqliteConnection conn, QuerySpec spec)
    {
        // --- Warm-up: 3 iterations of each (stabilize WASM JIT + page cache) ---
        for (int i = 0; i < WarmupIterations; i++)
        {
            DrainSharc(db, spec);
            DrainSqlite(conn, spec);
        }

        // --- Measure: 5 iterations of each, interleaved to level GC pressure ---
        Span<double> sharcTimes = stackalloc double[MeasuredIterations];
        Span<double> sqliteTimes = stackalloc double[MeasuredIterations];
        var sw = new Stopwatch();

        long sharcAllocBefore = 0, sharcAllocAfter = 0;
        long sqliteAllocBefore = 0, sqliteAllocAfter = 0;

        for (int i = 0; i < MeasuredIterations; i++)
        {
            // Measure Sharc
            if (i == 0) sharcAllocBefore = GC.GetAllocatedBytesForCurrentThread();
            sw.Restart();
            DrainSharc(db, spec);
            sw.Stop();
            if (i == 0) sharcAllocAfter = GC.GetAllocatedBytesForCurrentThread();
            sharcTimes[i] = sw.Elapsed.TotalMicroseconds();

            // Measure SQLite
            if (i == 0) sqliteAllocBefore = GC.GetAllocatedBytesForCurrentThread();
            sw.Restart();
            DrainSqlite(conn, spec);
            sw.Stop();
            if (i == 0) sqliteAllocAfter = GC.GetAllocatedBytesForCurrentThread();
            sqliteTimes[i] = sw.Elapsed.TotalMicroseconds();
        }

        // Take median of measured iterations (robust against GC spikes)
        sharcTimes.Sort();
        sqliteTimes.Sort();
        double sharcUs = sharcTimes[MeasuredIterations / 2];
        double sqliteUs = sqliteTimes[MeasuredIterations / 2];

        // --- Build result ---
        var sharcAlloc = sharcAllocAfter - sharcAllocBefore;
        var sqliteAlloc = sqliteAllocAfter - sqliteAllocBefore;

        string winner;
        double speedup;
        if (Math.Abs(sharcUs - sqliteUs) < Math.Max(sharcUs, sqliteUs) * 0.1)
        {
            winner = "tie";
            speedup = 1.0;
        }
        else if (sharcUs < sqliteUs)
        {
            winner = "sharc";
            speedup = Math.Round(sqliteUs / Math.Max(sharcUs, 0.1), 1);
        }
        else
        {
            winner = "sqlite";
            speedup = Math.Round(sharcUs / Math.Max(sqliteUs, 0.1), 1);
        }

        return new QueryResult
        {
            Id = spec.Id,
            Query = spec.Label,
            Description = spec.Description,
            SharcTimeUs = Math.Round(sharcUs, 1),
            SharcAlloc = FormatAlloc(sharcAlloc),
            SqliteTimeUs = Math.Round(sqliteUs, 1),
            SqliteAlloc = FormatAlloc(sqliteAlloc),
            Winner = winner,
            Speedup = speedup,
        };
    }

    private static void DrainSharc(SharcDatabase db, QuerySpec spec)
    {
        if (spec.Parameters is not null)
        {
            using var reader = db.Query(spec.Parameters, spec.SharqSql);
            while (reader.Read())
            {
                for (int i = 0; i < reader.FieldCount; i++)
                {
                    if (!reader.IsNull(i))
                        _ = reader.GetValue(i);
                }
            }
        }
        else
        {
            using var reader = db.Query(spec.SharqSql);
            while (reader.Read())
            {
                for (int i = 0; i < reader.FieldCount; i++)
                {
                    if (!reader.IsNull(i))
                        _ = reader.GetValue(i);
                }
            }
        }
    }

    private static void DrainSqlite(SqliteConnection conn, QuerySpec spec)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = spec.SqliteSql;

        if (spec.SqliteParams is not null)
        {
            foreach (var (name, value) in spec.SqliteParams)
                cmd.Parameters.AddWithValue(name, value);
        }

        using var reader = cmd.ExecuteReader();
        var fieldCount = reader.FieldCount;
        while (reader.Read())
        {
            for (int i = 0; i < fieldCount; i++)
            {
                if (!reader.IsDBNull(i))
                    _ = reader.GetValue(i);
            }
        }
    }

    private static string FormatAlloc(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        return $"{bytes / (1024.0 * 1024.0):F1} MB";
    }

    // ═══════════════════════════════════════════════════════════════
    //  Query Definitions — 13 queries adapted for Arena schema
    // ═══════════════════════════════════════════════════════════════

    private static readonly QuerySpec[] Queries =
    [
        new("q1", "SELECT *", "Full table scan",
            "SELECT * FROM users",
            "SELECT * FROM users"),

        new("q2", "WHERE age > 30", "Filtered scan with predicate",
            "SELECT id, name, age FROM users WHERE age > 30",
            "SELECT id, name, age FROM users WHERE age > 30"),

        new("q3", "WHERE + ORDER BY + LIMIT", "Sort with top-N",
            "SELECT id, name, score FROM users WHERE age >= 25 AND active = 1 ORDER BY score DESC LIMIT 100",
            "SELECT id, name, score FROM users WHERE age >= 25 AND active = 1 ORDER BY score DESC LIMIT 100"),

        new("q4", "GROUP BY + COUNT + AVG", "Aggregation pipeline",
            "SELECT dept, COUNT(*), AVG(score) FROM users GROUP BY dept",
            "SELECT dept, COUNT(*), AVG(score) FROM users GROUP BY dept"),

        new("q5", "UNION ALL", "Concatenate two result sets",
            "SELECT id, name, dept FROM users WHERE dept = 'eng' UNION ALL SELECT id, name, dept FROM users WHERE dept = 'ops'",
            "SELECT id, name, dept FROM users WHERE dept = 'eng' UNION ALL SELECT id, name, dept FROM users WHERE dept = 'ops'"),

        new("q6", "UNION (dedup)", "Deduplicated union",
            "SELECT id, name, dept FROM users WHERE age > 40 UNION SELECT id, name, dept FROM users WHERE score > 70",
            "SELECT id, name, dept FROM users WHERE age > 40 UNION SELECT id, name, dept FROM users WHERE score > 70"),

        new("q7", "INTERSECT", "Set intersection",
            "SELECT id, name FROM users WHERE age > 30 INTERSECT SELECT id, name FROM users WHERE score > 50",
            "SELECT id, name FROM users WHERE age > 30 INTERSECT SELECT id, name FROM users WHERE score > 50"),

        new("q8", "EXCEPT", "Set difference",
            "SELECT id, name FROM users WHERE age > 25 EXCEPT SELECT id, name FROM users WHERE dept = 'eng'",
            "SELECT id, name FROM users WHERE age > 25 EXCEPT SELECT id, name FROM users WHERE dept = 'eng'"),

        new("q9", "UNION ALL + ORDER + LIMIT", "Compound with top-N",
            "SELECT id, name, score FROM users WHERE dept = 'eng' UNION ALL SELECT id, name, score FROM users WHERE dept = 'ops' ORDER BY score DESC LIMIT 50",
            "SELECT id, name, score FROM users WHERE dept = 'eng' UNION ALL SELECT id, name, score FROM users WHERE dept = 'ops' ORDER BY score DESC LIMIT 50"),

        new("q10", "3-way UNION ALL", "Triple concatenation",
            "SELECT id, name FROM users WHERE dept = 'eng' UNION ALL SELECT id, name FROM users WHERE dept = 'ops' UNION ALL SELECT id, name FROM users WHERE dept = 'hr'",
            "SELECT id, name FROM users WHERE dept = 'eng' UNION ALL SELECT id, name FROM users WHERE dept = 'ops' UNION ALL SELECT id, name FROM users WHERE dept = 'hr'"),

        new("q11", "CTE \u2192 SELECT WHERE", "Common table expression + filter",
            "WITH active AS (SELECT id, name, score FROM users WHERE active = 1) SELECT id, name, score FROM active WHERE score > 50",
            "WITH active AS (SELECT id, name, score FROM users WHERE active = 1) SELECT id, name, score FROM active WHERE score > 50"),

        new("q12", "CTE + UNION ALL", "CTE with compound query",
            "WITH high AS (SELECT id, name FROM users WHERE score > 80) SELECT id, name FROM high UNION ALL SELECT id, name FROM users WHERE age < 25",
            "WITH high AS (SELECT id, name FROM users WHERE score > 80) SELECT id, name FROM high UNION ALL SELECT id, name FROM users WHERE age < 25"),

        new("q13", "Parameterized WHERE", "Prepared query with parameters",
            "SELECT id, name FROM users WHERE age > $min_age AND score < $max_score",
            "SELECT id, name FROM users WHERE age > $min_age AND score < $max_score",
            new Dictionary<string, object> { ["min_age"] = 25L, ["max_score"] = 75.0 },
            [("$min_age", (object)25), ("$max_score", (object)75.0)]),
    ];

    private sealed record QuerySpec(
        string Id,
        string Label,
        string Description,
        string SharqSql,
        string SqliteSql,
        Dictionary<string, object>? Parameters = null,
        (string Name, object Value)[]? SqliteParams = null);
}
