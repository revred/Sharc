// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Toolchains.InProcess.NoEmit;
using Sharc.Core.Trust;
using Sharc.Trust;

namespace Sharc.Benchmarks;

/// <summary>
/// Benchmarks for ledger append and integrity verification operations.
/// </summary>
[MemoryDiagnoser]
[Config(typeof(Config))]
public class LedgerBenchmark
{
    private sealed class Config : ManualConfig
    {
        public Config()
        {
            AddJob(Job.Default.WithToolchain(InProcessNoEmitToolchain.Instance));
        }
    }

    private string _dbPath = "";
    private SharcDatabase? _db;
    private LedgerManager? _ledger;
    private SharcSigner? _signer;

    [GlobalSetup]
    public void Setup()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"shard_ledger_bench_{Guid.NewGuid()}.db");

        // Create DB and Table manually
        using (var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={_dbPath}"))
        {
            connection.Open();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                PRAGMA page_size = 4096;
                CREATE TABLE _sharc_ledger (
                    seq INTEGER PRIMARY KEY,
                    ts INTEGER,
                    agent TEXT,
                    payload BLOB,
                    hash BLOB,
                    prev_hash BLOB,
                    sig BLOB
                );
            ";
            cmd.ExecuteNonQuery();
        }

        _db = SharcDatabase.Open(_dbPath, new SharcOpenOptions { Writable = true });
        _ledger = new LedgerManager(_db);
        _signer = new SharcSigner("bench-agent");

        // Register Agent
        var registry = new AgentRegistry(_db);
        var unsignedInfo = new AgentInfo(
            "bench-agent",
            AgentClass.User,
            _signer.GetPublicKey(),
            1_000_000,
            "*", "*",
            DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            DateTimeOffset.UtcNow.AddYears(1).ToUnixTimeSeconds(),
            "", false, Array.Empty<byte>());

        var verifyBuf = AgentRegistry.GetVerificationBuffer(unsignedInfo);
        var sig = _signer.Sign(verifyBuf);
        var agentInfo = unsignedInfo with { Signature = sig };
        registry.RegisterAgent(agentInfo);

        // Seed some data
        using var tx = _db.BeginTransaction();
        for (int i = 0; i < 1000; i++)
        {
            _ledger.Append($"Payload {i}", _signer, tx);
        }
        tx.Commit();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        try
        {
            _db?.Dispose();
            _signer?.Dispose();
            _db = null;
            _signer = null;

            GC.Collect();
            GC.WaitForPendingFinalizers();

            if (File.Exists(_dbPath))
            {
                File.Delete(_dbPath);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Cleanup warning: {ex.Message}");
        }
    }

    [Benchmark]
    public void VerifyIntegrity()
    {
        _ledger!.VerifyIntegrity();
    }

    [Benchmark]
    public void AppendSingle()
    {
        using var tx = _db!.BeginTransaction();
        _ledger!.Append("New Payload", _signer!, tx);
        tx.Commit();
    }
}
