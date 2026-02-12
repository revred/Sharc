/*-------------------------------------------------------------------------------------------------!
  "Where the mind is free to imagine and the craft is guided by clarity, code awakens."            |

  A collaborative work shaped by Artificial Intelligence and curated with intent by Ram Revanur.
  Software here is treated not as static text, but as a living system designed to learn and evolve.
  Built on the belief that architecture and context often define outcomes before code is written.

  This file reflects an AI-aware, agentic, context-driven, and continuously evolving approach
  to modern engineering. If you seek to transform a traditional codebase into an adaptive,
  intelligence-guided system, you may find resonance in these patterns and principles.

  Subtle conversations often begin with a single message Ã¢â‚¬â€ or a prompt with the right context.
  https://www.linkedin.com/in/revodoc/

  Licensed under the MIT License Ã¢â‚¬â€ free for personal and commercial use.                           |
--------------------------------------------------------------------------------------------------*/

using BenchmarkDotNet.Attributes;
using Sharc.Benchmarks.Helpers;
using Sharc.Core.Format;

namespace Sharc.Benchmarks.Micro;

/// <summary>
/// Micro-benchmarks for b-tree page header parsing.
/// Parse is called once per page visited; ReadCellPointers once per page to locate cells.
///
/// Memory profile:
///   Parse: 0 B allocated â€” returns readonly struct
///   ReadCellPointers: allocates ushort[cellCount] â€” the only allocation per page
///     5 cells  = ~34 B (ushort[5]  = 10 + 24 array overhead)
///     50 cells = ~124 B (ushort[50] = 100 + 24)
///     200 cells = ~424 B (ushort[200] = 400 + 24)
/// </summary>
[BenchmarkCategory("Micro", "Format", "BTree")]
[MemoryDiagnoser]
[DisassemblyDiagnoser(maxDepth: 3)]
public class BTreePageHeaderBenchmarks
{
    private byte[] _leafPage5Cells = null!;
    private byte[] _leafPage50Cells = null!;
    private byte[] _leafPage200Cells = null!;
    private byte[] _interiorPage10Cells = null!;
    private byte[] _leafIndexPage20 = null!;

    private BTreePageHeader _leafHeader5;
    private BTreePageHeader _leafHeader50;
    private BTreePageHeader _leafHeader200;
    private BTreePageHeader _interiorHeader10;

    [GlobalSetup]
    public void Setup()
    {
        _leafPage5Cells = ValidHeaderFactory.CreateLeafTablePage(5);
        _leafPage50Cells = ValidHeaderFactory.CreateLeafTablePage(50);
        _leafPage200Cells = ValidHeaderFactory.CreateLeafTablePage(200);
        _interiorPage10Cells = ValidHeaderFactory.CreateInteriorTablePage(10);

        // Leaf index page
        _leafIndexPage20 = ValidHeaderFactory.CreateLeafTablePage(20);
        _leafIndexPage20[0] = 0x0A; // leaf index type

        _leafHeader5 = BTreePageHeader.Parse(_leafPage5Cells);
        _leafHeader50 = BTreePageHeader.Parse(_leafPage50Cells);
        _leafHeader200 = BTreePageHeader.Parse(_leafPage200Cells);
        _interiorHeader10 = BTreePageHeader.Parse(_interiorPage10Cells);
    }

    // --- Parse: all 0 B allocated (readonly struct) ---

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Parse")]
    public BTreePageHeader Parse_LeafTable() => BTreePageHeader.Parse(_leafPage5Cells);

    [Benchmark]
    [BenchmarkCategory("Parse")]
    public BTreePageHeader Parse_InteriorTable() => BTreePageHeader.Parse(_interiorPage10Cells);

    [Benchmark]
    [BenchmarkCategory("Parse")]
    public BTreePageHeader Parse_LeafIndex() => BTreePageHeader.Parse(_leafIndexPage20);

    /// <summary>
    /// Parse 100 page headers. 0 B total allocated regardless of count.
    /// Struct returns avoid heap pressure entirely.
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("Parse", "Batch")]
    public int Parse_Batch100()
    {
        int sum = 0;
        for (int i = 0; i < 100; i++)
        {
            var hdr = BTreePageHeader.Parse(_leafPage50Cells);
            sum += hdr.CellCount;
        }
        return sum;
    }

    // --- ReadCellPointers: allocates ushort[cellCount] ---
    // MemoryDiagnoser will show exact allocation scaling

    /// <summary>5 cells: ~34 B allocated (ushort[5]).</summary>
    [Benchmark]
    [BenchmarkCategory("CellPointers")]
    public ushort[] ReadCellPointers_5Cells() => _leafHeader5.ReadCellPointers(_leafPage5Cells);

    /// <summary>50 cells: ~124 B allocated (ushort[50]).</summary>
    [Benchmark]
    [BenchmarkCategory("CellPointers")]
    public ushort[] ReadCellPointers_50Cells() => _leafHeader50.ReadCellPointers(_leafPage50Cells);

    /// <summary>200 cells: ~424 B allocated (ushort[200]).</summary>
    [Benchmark]
    [BenchmarkCategory("CellPointers")]
    public ushort[] ReadCellPointers_200Cells() => _leafHeader200.ReadCellPointers(_leafPage200Cells);

    [Benchmark]
    [BenchmarkCategory("CellPointers")]
    public ushort[] ReadCellPointers_Interior10() => _interiorHeader10.ReadCellPointers(_interiorPage10Cells);

    // --- Combined: Parse + ReadCellPointers (realistic usage pattern) ---

    /// <summary>
    /// Full page access: parse header (0 B) + read cell pointers (ushort[]).
    /// Total allocation is only the cell pointer array.
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("Combined")]
    public int ParseAndReadCells_50()
    {
        var hdr = BTreePageHeader.Parse(_leafPage50Cells);
        var pointers = hdr.ReadCellPointers(_leafPage50Cells);
        return pointers.Length;
    }

    /// <summary>
    /// Parse 10 pages + read cell pointers for each. Only allocation is 10 ushort[] arrays.
    /// No per-page overhead from the parse step itself.
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("Combined", "Batch")]
    public int ParseAndReadCells_Batch10()
    {
        int total = 0;
        for (int i = 0; i < 10; i++)
        {
            var hdr = BTreePageHeader.Parse(_leafPage50Cells);
            var pointers = hdr.ReadCellPointers(_leafPage50Cells);
            total += pointers.Length;
        }
        return total;
    }

    // --- Property accessors (verify JIT inlines these) ---

    [Benchmark]
    [BenchmarkCategory("Property")]
    public bool Property_IsLeaf()
    {
        var hdr = BTreePageHeader.Parse(_leafPage5Cells);
        return hdr.IsLeaf;
    }

    [Benchmark]
    [BenchmarkCategory("Property")]
    public bool Property_IsTable()
    {
        var hdr = BTreePageHeader.Parse(_leafPage5Cells);
        return hdr.IsTable;
    }

    [Benchmark]
    [BenchmarkCategory("Property")]
    public int Property_HeaderSize()
    {
        var hdr = BTreePageHeader.Parse(_interiorPage10Cells);
        return hdr.HeaderSize;
    }
}
