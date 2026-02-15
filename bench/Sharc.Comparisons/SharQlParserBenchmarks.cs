/*-------------------------------------------------------------------------------------------------!
  "Where the mind is free to imagine and the craft is guided by clarity, code awakens."            |

  A collaborative work shaped by Artificial Intelligence and curated with intent by Ram Revanur.
  Licensed under the MIT License — free for personal and commercial use.                           |
--------------------------------------------------------------------------------------------------*/

using BenchmarkDotNet.Attributes;
using Sharc.Query.Sharq;

namespace Sharc.Comparisons;

/// <summary>
/// Benchmarks for the Sharq zero-allocation recursive descent parser.
/// Measures parse throughput across query complexities — from trivial to multi-hop graph traversals.
///
/// The parser is a ref struct with on-demand tokenization: zero heap allocation during scan,
/// allocations only at the AST boundary (identifier strings + node objects).
///
/// Run with:
///   dotnet run -c Release --project bench/Sharc.Comparisons -- --filter *SharqParser*
/// </summary>
[MemoryDiagnoser]
[RankColumn]
public class SharqParserBenchmarks
{
    // ═══════════════════════════════════════════════════════════════
    //  Query strings — from simple to brutally complex
    // ═══════════════════════════════════════════════════════════════

    private const string Simple = "SELECT * FROM users";

    private const string WithWhere =
        "SELECT id, name, age FROM users WHERE age > 18";

    private const string Medium =
        "SELECT id, name, email FROM users WHERE age >= 18 AND status = 'active' ORDER BY name ASC";

    private const string FullPipeline =
        "SELECT department, count(*) AS total, sum(amount) AS spent " +
        "FROM orders " +
        "WHERE status = 'completed' AND amount > 100 " +
        "GROUP BY department " +
        "ORDER BY spent DESC " +
        "LIMIT 50 OFFSET 10";

    private const string EdgeForward =
        "SELECT |>order|>product.* FROM person:billy";

    private const string EdgeMultiHop =
        "SELECT |>order|>product<|order<|person.name FROM person:alice";

    private const string EdgeBidirectional =
        "SELECT <|>friends_with<|>person.name FROM person:alice";

    private const string EdgeInWhere =
        "SELECT * FROM agents WHERE count(|>attests) > 3";

    private const string ComplexWhere =
        "SELECT * FROM users WHERE (age > 18 AND age < 65) OR (status = 'vip' AND score >= 95.5) " +
        "AND name LIKE 'A%' AND dept NOT IN ('hr', 'legal', 'ops')";

    private const string DeepArithmetic =
        "SELECT (a + b) * (c - d) / (e % f + 1) - (g * h) + (i / j) FROM calc";

    private string _monster = null!;

    [GlobalSetup]
    public void Setup()
    {
        // Build a monster query: 50-column SELECT with complex WHERE, GROUP BY, ORDER BY
        var columns = string.Join(", ", Enumerable.Range(0, 50).Select(i => $"col{i}"));
        var conditions = string.Join(" AND ", Enumerable.Range(0, 20).Select(i => $"col{i} > {i * 10}"));
        var groupBy = string.Join(", ", Enumerable.Range(0, 5).Select(i => $"col{i}"));
        var orderBy = string.Join(", ", Enumerable.Range(0, 5).Select(i => $"col{i} DESC"));

        _monster = $"SELECT {columns} FROM mega_table WHERE {conditions} " +
                   $"GROUP BY {groupBy} ORDER BY {orderBy} LIMIT 1000 OFFSET 500";
    }

    // ═══════════════════════════════════════════════════════════════
    //  SELECT statement benchmarks — escalating complexity
    // ═══════════════════════════════════════════════════════════════

    [Benchmark(Description = "SELECT * FROM t")]
    [BenchmarkCategory("Parse")]
    public object Parse_Simple() => SharqParser.Parse(Simple);

    [Benchmark(Description = "SELECT cols WHERE col > N")]
    [BenchmarkCategory("Parse")]
    public object Parse_WithWhere() => SharqParser.Parse(WithWhere);

    [Benchmark(Description = "SELECT .. WHERE AND .. ORDER BY")]
    [BenchmarkCategory("Parse")]
    public object Parse_Medium() => SharqParser.Parse(Medium);

    [Benchmark(Description = "Full pipeline (GROUP/ORDER/LIMIT)")]
    [BenchmarkCategory("Parse")]
    public object Parse_FullPipeline() => SharqParser.Parse(FullPipeline);

    [Benchmark(Description = "50 cols + 20 ANDs + GROUP + ORDER")]
    [BenchmarkCategory("Parse")]
    public object Parse_Monster() => SharqParser.Parse(_monster);

    // ═══════════════════════════════════════════════════════════════
    //  Edge traversal benchmarks — shark tooth operators
    // ═══════════════════════════════════════════════════════════════

    [Benchmark(Description = "|>edge|>target.* (forward)")]
    [BenchmarkCategory("Edge")]
    public object Parse_EdgeForward() => SharqParser.Parse(EdgeForward);

    [Benchmark(Description = "4-hop mixed edge chain")]
    [BenchmarkCategory("Edge")]
    public object Parse_EdgeMultiHop() => SharqParser.Parse(EdgeMultiHop);

    [Benchmark(Description = "<|>bidi edge")]
    [BenchmarkCategory("Edge")]
    public object Parse_EdgeBidirectional() => SharqParser.Parse(EdgeBidirectional);

    [Benchmark(Description = "count(|>edge) in WHERE")]
    [BenchmarkCategory("Edge")]
    public object Parse_EdgeInWhere() => SharqParser.Parse(EdgeInWhere);

    // ═══════════════════════════════════════════════════════════════
    //  Expression benchmarks — raw expression parsing speed
    // ═══════════════════════════════════════════════════════════════

    [Benchmark(Description = "Complex WHERE (OR/AND/LIKE/IN)")]
    [BenchmarkCategory("Expression")]
    public object Parse_ComplexWhere() => SharqParser.Parse(ComplexWhere);

    [Benchmark(Description = "Deep arithmetic ((a+b)*(c-d)/...)")]
    [BenchmarkCategory("Expression")]
    public object Parse_DeepArithmetic() => SharqParser.Parse(DeepArithmetic);

    // ═══════════════════════════════════════════════════════════════
    //  Throughput: parse same query 1000x to measure sustained rate
    // ═══════════════════════════════════════════════════════════════

    [Benchmark(Description = "1000x simple parse (throughput)")]
    [BenchmarkCategory("Throughput")]
    public int Parse_Throughput_Simple()
    {
        int count = 0;
        for (int i = 0; i < 1000; i++)
        {
            _ = SharqParser.Parse(Simple);
            count++;
        }
        return count;
    }

    [Benchmark(Description = "1000x full pipeline (throughput)")]
    [BenchmarkCategory("Throughput")]
    public int Parse_Throughput_FullPipeline()
    {
        int count = 0;
        for (int i = 0; i < 1000; i++)
        {
            _ = SharqParser.Parse(FullPipeline);
            count++;
        }
        return count;
    }

    // ═══════════════════════════════════════════════════════════════
    //  Expression-only parse (no SELECT/FROM overhead)
    // ═══════════════════════════════════════════════════════════════

    private const string ExprArithmetic = "1 + 2 * 3 - 4 / 5 + 6 % 7";
    private const string ExprLogical = "a > 1 AND (b < 2 OR c = 3) AND NOT d IS NULL";
    private const string ExprEdgeChain = "person:alice|>knows|>person<|order<|product.name";

    [Benchmark(Description = "Expr: arithmetic")]
    [BenchmarkCategory("ExprOnly")]
    public object ParseExpr_Arithmetic() => SharqParser.ParseExpression(ExprArithmetic);

    [Benchmark(Description = "Expr: logical + IS NULL")]
    [BenchmarkCategory("ExprOnly")]
    public object ParseExpr_Logical() => SharqParser.ParseExpression(ExprLogical);

    [Benchmark(Description = "Expr: record:id edge chain")]
    [BenchmarkCategory("ExprOnly")]
    public object ParseExpr_EdgeChain() => SharqParser.ParseExpression(ExprEdgeChain);
}
