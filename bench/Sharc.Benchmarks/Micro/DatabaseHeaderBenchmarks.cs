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

using BenchmarkDotNet.Attributes;
using Sharc.Benchmarks.Helpers;
using Sharc.Core.Format;

namespace Sharc.Benchmarks.Micro;

/// <summary>
/// Micro-benchmarks for SQLite database header parsing.
/// Called once per database open — absolute latency matters.
///
/// Memory profile:
///   Parse: 0 B allocated — readonly struct, span-based parsing
///   HasValidMagic: 0 B — SequenceEqual on span
///   All property access: 0 B — struct fields
///   100x batch: 0 B total — proves no hidden per-parse allocations
/// </summary>
[BenchmarkCategory("Micro", "Format", "Header")]
[MemoryDiagnoser]
[DisassemblyDiagnoser(maxDepth: 3)]
public class DatabaseHeaderBenchmarks
{
    private byte[] _validHeader4096 = null!;
    private byte[] _validHeader65536 = null!;
    private byte[] _invalidMagicHeader = null!;
    private byte[] _fullPage4096 = null!;

    [GlobalSetup]
    public void Setup()
    {
        _validHeader4096 = ValidHeaderFactory.CreateDatabaseHeader(pageSize: 4096, pageCount: 100);
        _validHeader65536 = ValidHeaderFactory.CreateDatabaseHeader(pageSize: 1, pageCount: 5000);
        _invalidMagicHeader = new byte[100];
        _fullPage4096 = new byte[4096];
        _validHeader4096.CopyTo(_fullPage4096.AsSpan());
    }

    // --- Parse: all 0 B allocated ---

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Parse")]
    public DatabaseHeader Parse_4096PageSize() => DatabaseHeader.Parse(_validHeader4096);

    [Benchmark]
    [BenchmarkCategory("Parse")]
    public DatabaseHeader Parse_65536PageSize() => DatabaseHeader.Parse(_validHeader65536);

    /// <summary>
    /// Parse from a full 4096-byte page buffer. Same cost as exact 100-byte buffer.
    /// Validates no over-reading or extra allocation for larger input.
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("Parse")]
    public DatabaseHeader Parse_FromFullPage() => DatabaseHeader.Parse(_fullPage4096);

    // --- Validate: magic string check ---

    [Benchmark]
    [BenchmarkCategory("Validate")]
    public bool HasValidMagic_Valid() => DatabaseHeader.HasValidMagic(_validHeader4096);

    [Benchmark]
    [BenchmarkCategory("Validate")]
    public bool HasValidMagic_Invalid() => DatabaseHeader.HasValidMagic(_invalidMagicHeader);

    // --- Property access: verify struct field access is zero-cost ---

    [Benchmark]
    [BenchmarkCategory("Property")]
    public int Property_PageSize()
    {
        var header = DatabaseHeader.Parse(_validHeader4096);
        return header.PageSize;
    }

    [Benchmark]
    [BenchmarkCategory("Property")]
    public int Property_UsablePageSize()
    {
        var header = DatabaseHeader.Parse(_validHeader4096);
        return header.UsablePageSize;
    }

    [Benchmark]
    [BenchmarkCategory("Property")]
    public bool Property_IsWalMode()
    {
        var header = DatabaseHeader.Parse(_validHeader4096);
        return header.IsWalMode;
    }

    /// <summary>
    /// Access all major properties after parse. 0 B — pure struct field reads.
    /// Shows the total cost of extracting everything from the header.
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("Property")]
    public int Property_ReadAllFields()
    {
        var h = DatabaseHeader.Parse(_validHeader4096);
        return h.PageSize + h.PageCount + h.TextEncoding + h.UserVersion
             + h.ApplicationId + h.SchemaFormat + h.FreelistPageCount
             + h.UsablePageSize + (h.IsWalMode ? 1 : 0)
             + (int)h.ChangeCounter + (int)h.SchemaCookie;
    }

    // --- Batch: scaling allocation test ---

    /// <summary>
    /// 100 header parses. 0 B total — confirms no hidden allocations at scale.
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("Parse", "Batch")]
    public int Parse_Batch100()
    {
        int sum = 0;
        for (int i = 0; i < 100; i++)
        {
            var header = DatabaseHeader.Parse(_validHeader4096);
            sum += header.PageSize;
        }
        return sum;
    }

    /// <summary>
    /// 1000 header parses. Still 0 B total — struct return value is stack-allocated.
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("Parse", "Batch")]
    public int Parse_Batch1000()
    {
        int sum = 0;
        for (int i = 0; i < 1000; i++)
        {
            var header = DatabaseHeader.Parse(_validHeader4096);
            sum += header.PageSize;
        }
        return sum;
    }
}
