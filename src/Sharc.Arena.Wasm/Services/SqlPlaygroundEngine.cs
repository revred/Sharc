// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using System.Diagnostics;
using Microsoft.Data.Sqlite;
using Sharc.Arena.Wasm.Models;

namespace Sharc.Arena.Wasm.Services;

/// <summary>
/// Live SQL Playground engine: executes arbitrary SQL on both Sharc and SQLite,
/// times the execution, and returns results in an Airtable-like grid.
/// </summary>
public sealed class SqlPlaygroundEngine : IDisposable
{
    private readonly DataGenerator _dataGen;
    private byte[]? _dbBytes;
    private SharcDatabase? _sharcDb;
    private SqliteConnection? _sqliteConn;
    private string? _tempPath;
    private int _rowCount = 2500;
    private int _nodeCount = 500;

    /// <summary>Maximum rows returned in the result grid to prevent OOM.</summary>
    private const int MaxGridRows = 200;

    public SqlPlaygroundEngine(DataGenerator dataGen) => _dataGen = dataGen;

    /// <summary>
    /// Sets the workload size and forces re-initialization on next query.
    /// </summary>
    public void SetWorkloadSize(int rowCount, int nodeCount)
    {
        if (_rowCount == rowCount && _nodeCount == nodeCount) return;
        _rowCount = rowCount;
        _nodeCount = nodeCount;
        Cleanup();
    }

    /// <summary>Current workload row count.</summary>
    public int WorkloadRowCount => _rowCount;

    private void EnsureInitialized()
    {
        if (_sharcDb is not null && _sqliteConn is not null) return;

        Cleanup();

        _dbBytes = _dataGen.GenerateDatabase(_rowCount, _nodeCount);

        // Sharc: open from memory bytes
        _sharcDb = SharcDatabase.OpenMemory(_dbBytes);

        // SQLite: write to temp file, open connection
        _tempPath = Path.GetTempFileName();
        File.WriteAllBytes(_tempPath, _dbBytes);
        _sqliteConn = new SqliteConnection($"Data Source={_tempPath};Mode=ReadOnly");
        _sqliteConn.Open();
    }

    /// <summary>
    /// Executes the given SQL on both Sharc and SQLite, returning timing,
    /// allocation, and result grid data.
    /// </summary>
    public PlaygroundResult ExecuteQuery(string sql)
    {
        EnsureInitialized();

        var (sharcTimeUs, sharcAlloc, sharcRowCount, columns, rows, sharcError) = RunSharc(sql);
        var (sqliteTimeUs, sqliteAlloc, sqliteRowCount, sqliteError) = RunSqlite(sql);

        // If Sharc failed but SQLite succeeded, try to get grid data from SQLite
        if (sharcError is not null && sqliteError is null)
        {
            (columns, rows) = ReadSqliteGrid(sql);
        }

        return new PlaygroundResult
        {
            Sql = sql,
            SharcTimeUs = sharcTimeUs,
            SharcAlloc = sharcAlloc,
            SharcRowCount = sharcRowCount,
            SharcError = sharcError,
            SqliteTimeUs = sqliteTimeUs,
            SqliteAlloc = sqliteAlloc,
            SqliteRowCount = sqliteRowCount,
            SqliteError = sqliteError,
            ColumnNames = columns,
            Rows = rows,
        };
    }

    private (double timeUs, string alloc, long rowCount, List<string> columns, List<List<object?>> rows, string? error)
        RunSharc(string sql)
    {
        try
        {
            // Warmup pass
            using (var warmup = _sharcDb!.Query(sql))
            {
                while (warmup.Read()) { }
            }

            var allocBefore = GC.GetAllocatedBytesForCurrentThread();
            var sw = Stopwatch.StartNew();

            using var reader = _sharcDb.Query(sql);

            // Read column names
            var columns = new List<string>(reader.FieldCount);
            for (int i = 0; i < reader.FieldCount; i++)
                columns.Add(reader.GetColumnName(i));

            // Read rows (cap at MaxGridRows)
            var rows = new List<List<object?>>();
            long rowCount = 0;
            while (reader.Read())
            {
                rowCount++;
                if (rows.Count < MaxGridRows)
                {
                    var row = new List<object?>(reader.FieldCount);
                    for (int i = 0; i < reader.FieldCount; i++)
                    {
                        row.Add(reader.IsNull(i) ? null : reader.GetValue(i));
                    }
                    rows.Add(row);
                }
            }

            sw.Stop();
            var allocAfter = GC.GetAllocatedBytesForCurrentThread();

            return (
                Math.Round(sw.Elapsed.TotalMicroseconds(), 1),
                FormatAlloc(allocAfter - allocBefore),
                rowCount,
                columns,
                rows,
                null
            );
        }
        catch (Exception ex)
        {
            return (0, "0 B", 0, [], [], ex.Message);
        }
    }

    private (double timeUs, string alloc, long rowCount, string? error) RunSqlite(string sql)
    {
        try
        {
            // Warmup pass
            using (var warmupCmd = _sqliteConn!.CreateCommand())
            {
                warmupCmd.CommandText = sql;
                using var warmupReader = warmupCmd.ExecuteReader();
                while (warmupReader.Read()) { }
            }

            var allocBefore = GC.GetAllocatedBytesForCurrentThread();
            var sw = Stopwatch.StartNew();

            using var cmd = _sqliteConn.CreateCommand();
            cmd.CommandText = sql;
            using var reader = cmd.ExecuteReader();

            long rowCount = 0;
            while (reader.Read())
            {
                // Touch all columns to ensure fair comparison
                for (int i = 0; i < reader.FieldCount; i++)
                    _ = reader.GetValue(i);
                rowCount++;
            }

            sw.Stop();
            var allocAfter = GC.GetAllocatedBytesForCurrentThread();

            return (
                Math.Round(sw.Elapsed.TotalMicroseconds(), 1),
                FormatAlloc(allocAfter - allocBefore),
                rowCount,
                null
            );
        }
        catch (Exception ex)
        {
            return (0, "0 B", 0, ex.Message);
        }
    }

    /// <summary>
    /// Reads grid data from SQLite (fallback when Sharc fails but SQLite succeeds).
    /// </summary>
    private (List<string> columns, List<List<object?>> rows) ReadSqliteGrid(string sql)
    {
        try
        {
            using var cmd = _sqliteConn!.CreateCommand();
            cmd.CommandText = sql;
            using var reader = cmd.ExecuteReader();

            var columns = new List<string>(reader.FieldCount);
            for (int i = 0; i < reader.FieldCount; i++)
                columns.Add(reader.GetName(i));

            var rows = new List<List<object?>>();
            while (reader.Read() && rows.Count < MaxGridRows)
            {
                var row = new List<object?>(reader.FieldCount);
                for (int i = 0; i < reader.FieldCount; i++)
                    row.Add(reader.IsDBNull(i) ? null : reader.GetValue(i));
                rows.Add(row);
            }

            return (columns, rows);
        }
        catch
        {
            return ([], []);
        }
    }

    private void Cleanup()
    {
        _sharcDb?.Dispose();
        _sharcDb = null;

        if (_sqliteConn is not null)
        {
            _sqliteConn.Close();
            _sqliteConn.Dispose();
            _sqliteConn = null;
        }

        if (_tempPath is not null)
        {
            try { File.Delete(_tempPath); } catch { /* ignore cleanup errors */ }
            _tempPath = null;
        }

        _dbBytes = null;
    }

    private static string FormatAlloc(long bytes)
    {
        if (bytes < 0) bytes = 0; // GC can report negative in WASM
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        return $"{bytes / (1024.0 * 1024.0):F1} MB";
    }

    public void Dispose() => Cleanup();
}
