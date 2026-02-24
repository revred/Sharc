using System.Diagnostics;
using System.Linq;
using Microsoft.Data.Sqlite;
using Sharc;
using Sharc.Core.Primitives;
using Sharc.Core.Query;
using Sharc.Core.Schema;
using Sharc.IntegrationTests.Helpers;
using Xunit;
using Xunit.Abstractions;

namespace Sharc.IntegrationTests;

public class GuidPerformanceComparisonTests
{
    private readonly ITestOutputHelper _output;

    public GuidPerformanceComparisonTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void Benchmark_Guid_Filtering_Stable()
    {
        const int rowCount = 500_000;
        const int iterations = 10;
        var targetGuid = Guid.NewGuid();
        var (hi, lo) = GuidCodec.ToInt64Pair(targetGuid);
        int targetRow = rowCount - 1;

        _output.WriteLine($"Generating {rowCount} rows in dual tables...");
        var dbBytes = TestDatabaseFactory.CreateDatabaseWith(conn =>
        {
            TestDatabaseFactory.Execute(conn, "CREATE TABLE old (id INTEGER PRIMARY KEY, gh INTEGER, gl INTEGER);");
            TestDatabaseFactory.Execute(conn, "CREATE TABLE new (id INTEGER PRIMARY KEY, g__hi INTEGER, g__lo INTEGER);");

            using var trans = conn.BeginTransaction();
            
            using var insOld = conn.CreateCommand();
            insOld.CommandText = "INSERT INTO old (id, gh, gl) VALUES ($id, $hi, $lo);";
            var idOld = insOld.Parameters.Add("$id", SqliteType.Integer);
            var hiOld = insOld.Parameters.Add("$hi", SqliteType.Integer);
            var loOld = insOld.Parameters.Add("$lo", SqliteType.Integer);

            using var insNew = conn.CreateCommand();
            insNew.CommandText = "INSERT INTO new (id, g__hi, g__lo) VALUES ($id, $hi, $lo);";
            var idNew = insNew.Parameters.Add("$id", SqliteType.Integer);
            var hiNew = insNew.Parameters.Add("$hi", SqliteType.Integer);
            var loNew = insNew.Parameters.Add("$lo", SqliteType.Integer);

            for (int i = 0; i < rowCount; i++)
            {
                long hv = i == targetRow ? hi : (long)i;
                long lv = i == targetRow ? lo : (long)i;
                idOld.Value = i; hiOld.Value = hv; loOld.Value = lv; insOld.ExecuteNonQuery();
                idNew.Value = i; hiNew.Value = hv; loNew.Value = lv; insNew.ExecuteNonQuery();
            }
            trans.Commit();
        });

        var dbPath = Path.GetTempFileName();
        File.WriteAllBytes(dbPath, dbBytes);
        using var db = SharcDatabase.Open(dbPath);

        // Resolve Ordinals
        var tOld = db.Schema.GetTable("old");
        int ghOrd = tOld.GetColumnOrdinal("gh");
        int glOrd = tOld.GetColumnOrdinal("gl");

        var tNew = db.Schema.GetTable("new");
        var gCol = tNew.Columns.First(c => c.Name == "g");
        int hiPhys = gCol.MergedPhysicalOrdinals[0];
        int loPhys = gCol.MergedPhysicalOrdinals[1];

        // --- JIT ---
        var fOld = FilterStar.And(FilterStar.Column("gh").Eq(hi), FilterStar.Column("gl").Eq(lo));
        var fNew = FilterStar.Column("g").Eq(targetGuid);

        var qOld = db.Jit("old").Where(fOld);
        var qNew = db.Jit("new").Where(fNew);

        // Warmup
        using (var r = qOld.Query()) while(r.Read());
        using (var r = qNew.Query()) while(r.Read());

        var swOldJit = Stopwatch.StartNew();
        for(int i=0; i<iterations; i++)
        {
            using var reader = qOld.Query();
            while(reader.Read());
        }
        swOldJit.Stop();

        var swNewJit = Stopwatch.StartNew();
        for(int i=0; i<iterations; i++)
        {
            using var reader = qNew.Query();
            while(reader.Read());
        }
        swNewJit.Stop();

        _output.WriteLine("--- JIT Execution (500k rows x 10 iterations) ---");
        _output.WriteLine($"Old (Composite): {swOldJit.ElapsedMilliseconds}ms");
        _output.WriteLine($"New (Native):    {swNewJit.ElapsedMilliseconds}ms");
        _output.WriteLine($"Reduction:       {((double)swOldJit.ElapsedMilliseconds - swNewJit.ElapsedMilliseconds) / swOldJit.ElapsedMilliseconds * 100:F2}%");

        // --- Interpreted ---
        var nOld = new AndNode([
            new PredicateNode(ghOrd, FilterOp.Eq, TypedFilterValue.FromInt64(hi)),
            new PredicateNode(glOrd, FilterOp.Eq, TypedFilterValue.FromInt64(lo))
        ]);
        var nNew = new GuidPredicateNode(hiPhys, loPhys, FilterOp.Eq, TypedFilterValue.FromGuid(targetGuid));

        // Warmup
        using (var r = db.CreateReader("old", nOld)) while(r.Read());
        using (var r = db.CreateReader("new", nNew)) while(r.Read());

        var swOldInt = Stopwatch.StartNew();
        for(int i=0; i<iterations; i++)
        {
            using var reader = db.CreateReader("old", nOld);
            while(reader.Read());
        }
        swOldInt.Stop();

        var swNewInt = Stopwatch.StartNew();
        for(int i=0; i<iterations; i++)
        {
            using var reader = db.CreateReader("new", nNew);
            while(reader.Read());
        }
        swNewInt.Stop();

        _output.WriteLine("--- Interpreted Execution (500k rows x 10 iterations) ---");
        _output.WriteLine($"Old (AndNode):   {swOldInt.ElapsedMilliseconds}ms");
        _output.WriteLine($"New (GuidNode):  {swNewInt.ElapsedMilliseconds}ms");
        _output.WriteLine($"Reduction:       {((double)swOldInt.ElapsedMilliseconds - swNewInt.ElapsedMilliseconds) / swOldInt.ElapsedMilliseconds * 100:F2}%");
    }
}
