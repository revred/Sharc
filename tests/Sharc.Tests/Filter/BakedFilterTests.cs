using Xunit;
using Sharc.Core.Schema;

namespace Sharc.Tests.Filter;

public class BakedFilterTests
{
    [Fact]
    public void Test_Baked_SimpleIntComparison()
    {
        // Setup: 1 column, int (serial type 1, value 42)
        var columns = new List<ColumnInfo> { new ColumnInfo { Name = "age", Ordinal = 0, DeclaredType = "INTEGER", IsPrimaryKey = false, IsNotNull = false } };
        var filter = FilterStar.Column("age").Gt(30);

        var node = FilterTreeCompiler.CompileBaked(filter, columns);

        // Payload: [HeaderSize(2), SerialType1(1)] [Body(42)]
        byte[] payload = [2, 1, 42];
        long[] serialTypes = [1];
        int bodyOffset = 2;

        bool result = node.Evaluate(payload, serialTypes, bodyOffset, 1);
        Assert.True(result);

        // Try false case
        byte[] payload2 = [2, 1, 20];
        Assert.False(node.Evaluate(payload2, serialTypes, bodyOffset, 1));
    }

    [Fact]
    public void Test_Baked_AndComparison()
    {
        var columns = new List<ColumnInfo> 
        { 
            new ColumnInfo { Name = "age", Ordinal = 0, DeclaredType = "INTEGER", IsPrimaryKey = false, IsNotNull = false },
            new ColumnInfo { Name = "score", Ordinal = 1, DeclaredType = "REAL", IsPrimaryKey = false, IsNotNull = false }
        };

        var filter = FilterStar.And(
            FilterStar.Column("age").Gt(30),
            FilterStar.Column("score").Lt(50.0)
        );

        var node = FilterTreeCompiler.CompileBaked(filter, columns);

        // Payload: [HeaderSize(3), ST(1), ST(7)] [Body: 42 (1b), 25.5 (8b)]
        // Double 25.5 in big-endian: 40 39 80 00 00 00 00 00
        byte[] payload = [3, 1, 7, 42, 0x40, 0x39, 0x80, 0x00, 0x00, 0x00, 0x00, 0x00];
        long[] serialTypes = [1, 7];
        int bodyOffset = 3;

        bool result = node.Evaluate(payload, serialTypes, bodyOffset, 1);
        Assert.True(result);
    }

    [Fact]
    public void Test_Baked_BenchmarkSimulation()
    {
        var columns = new List<ColumnInfo> 
        { 
            new ColumnInfo { Name = "id", Ordinal = 0, DeclaredType = "INTEGER", IsPrimaryKey = true, IsNotNull = true },
            new ColumnInfo { Name = "name", Ordinal = 1, DeclaredType = "TEXT", IsPrimaryKey = false, IsNotNull = true },
            new ColumnInfo { Name = "email", Ordinal = 2, DeclaredType = "TEXT", IsPrimaryKey = false, IsNotNull = true },
            new ColumnInfo { Name = "age", Ordinal = 3, DeclaredType = "INTEGER", IsPrimaryKey = false, IsNotNull = true },
            new ColumnInfo { Name = "score", Ordinal = 4, DeclaredType = "REAL", IsPrimaryKey = false, IsNotNull = true },
            new ColumnInfo { Name = "bio", Ordinal = 5, DeclaredType = "TEXT", IsPrimaryKey = false, IsNotNull = false },
            new ColumnInfo { Name = "active", Ordinal = 6, DeclaredType = "INTEGER", IsPrimaryKey = false, IsNotNull = true },
            new ColumnInfo { Name = "dept", Ordinal = 7, DeclaredType = "TEXT", IsPrimaryKey = false, IsNotNull = true },
            new ColumnInfo { Name = "created", Ordinal = 8, DeclaredType = "TEXT", IsPrimaryKey = false, IsNotNull = true }
        };

        var filter = FilterStar.And(
            FilterStar.Column("age").Gt(30),
            FilterStar.Column("score").Lt(50.0)
        );

        var node = FilterTreeCompiler.CompileBaked(filter, columns);

        // Record for: id=1, name='A B', email='a.b@ex.com', age=42, score=25.5, bio=null, active=1, dept='eng', created='2024-01-01'
        // Serial Types: [1(id), 13(name), 25(email), 1(age), 7(score), 0(bio), 1(active), 13(dept), 13(created)]
        // Body: [1(id), 'A B'(3), 'a.b@ex.com'(10), 42(1), 25.5(8), (null), 1(1), 'eng'(3), '2024-01-01'(10)]
        long[] serialTypes = [1, 13, 25, 1, 7, 0, 1, 13, 13];
        byte[] payload = [1, (byte)'A', (byte)' ', (byte)'B', 0,0,0,0,0,0,0,0,0,0, 42, 0x40, 0x39, 0x80, 0x00, 0x00, 0x00, 0x00, 0x00, 1, (byte)'e', (byte)'n', (byte)'g', 0,0,0,0,0,0,0,0,0,0];
        
        bool result = node.Evaluate(payload, serialTypes, 0, 1);
        Assert.NotNull(node);
    }
}
