using Sharc.Core.Schema;
using Xunit;

namespace Sharc.Tests.Schema;

public class CreateIndexParserTests
{
    [Fact]
    public void ParseColumns_SingleColumn_ReturnsOneColumn()
    {
        var columns = SchemaParser.ParseIndexColumns("CREATE INDEX idx_name ON users (name)");

        Assert.Single(columns);
        Assert.Equal("name", columns[0].Name);
        Assert.Equal(0, columns[0].Ordinal);
        Assert.False(columns[0].IsDescending);
    }

    [Fact]
    public void ParseColumns_MultipleColumns_ReturnsAllInOrder()
    {
        var columns = SchemaParser.ParseIndexColumns(
            "CREATE INDEX idx_multi ON events (user_id, created_at, event_type)");

        Assert.Equal(3, columns.Count);
        Assert.Equal("user_id", columns[0].Name);
        Assert.Equal(0, columns[0].Ordinal);
        Assert.Equal("created_at", columns[1].Name);
        Assert.Equal(1, columns[1].Ordinal);
        Assert.Equal("event_type", columns[2].Name);
        Assert.Equal(2, columns[2].Ordinal);
    }

    [Fact]
    public void ParseColumns_ExplicitAsc_NotDescending()
    {
        var columns = SchemaParser.ParseIndexColumns(
            "CREATE INDEX idx ON t (col ASC)");

        Assert.Single(columns);
        Assert.Equal("col", columns[0].Name);
        Assert.False(columns[0].IsDescending);
    }

    [Fact]
    public void ParseColumns_ExplicitDesc_IsDescending()
    {
        var columns = SchemaParser.ParseIndexColumns(
            "CREATE INDEX idx ON t (col DESC)");

        Assert.Single(columns);
        Assert.Equal("col", columns[0].Name);
        Assert.True(columns[0].IsDescending);
    }

    [Fact]
    public void ParseColumns_MixedSortOrder_ParsesCorrectly()
    {
        var columns = SchemaParser.ParseIndexColumns(
            "CREATE INDEX idx ON t (a ASC, b DESC, c)");

        Assert.Equal(3, columns.Count);
        Assert.False(columns[0].IsDescending); // a ASC
        Assert.True(columns[1].IsDescending);   // b DESC
        Assert.False(columns[2].IsDescending); // c (default ASC)
    }

    [Fact]
    public void ParseColumns_UniqueIndex_ParsesColumns()
    {
        var columns = SchemaParser.ParseIndexColumns(
            "CREATE UNIQUE INDEX idx_email ON users (email)");

        Assert.Single(columns);
        Assert.Equal("email", columns[0].Name);
    }

    [Fact]
    public void ParseColumns_IfNotExists_ParsesColumns()
    {
        var columns = SchemaParser.ParseIndexColumns(
            "CREATE INDEX IF NOT EXISTS idx ON t (x, y)");

        Assert.Equal(2, columns.Count);
        Assert.Equal("x", columns[0].Name);
        Assert.Equal("y", columns[1].Name);
    }

    [Fact]
    public void ParseColumns_QuotedIdentifiers_UnquotesCorrectly()
    {
        var columns = SchemaParser.ParseIndexColumns(
            "CREATE INDEX \"my_idx\" ON \"my table\" (\"my col\", [another col])");

        Assert.Equal(2, columns.Count);
        Assert.Equal("my col", columns[0].Name);
        Assert.Equal("another col", columns[1].Name);
    }

    [Fact]
    public void ParseColumns_BacktickQuotes_UnquotesCorrectly()
    {
        var columns = SchemaParser.ParseIndexColumns(
            "CREATE INDEX idx ON t (`col name`)");

        Assert.Single(columns);
        Assert.Equal("col name", columns[0].Name);
    }

    [Fact]
    public void ParseColumns_PartialIndex_IgnoresWhereClause()
    {
        var columns = SchemaParser.ParseIndexColumns(
            "CREATE INDEX idx ON t (status) WHERE status > 0");

        Assert.Single(columns);
        Assert.Equal("status", columns[0].Name);
    }

    [Fact]
    public void ParseColumns_EmptySql_ReturnsEmpty()
    {
        var columns = SchemaParser.ParseIndexColumns("");

        Assert.Empty(columns);
    }

    [Fact]
    public void ParseColumns_NoParentheses_ReturnsEmpty()
    {
        var columns = SchemaParser.ParseIndexColumns("CREATE INDEX idx ON t");

        Assert.Empty(columns);
    }

    [Fact]
    public void ParseColumns_CaseInsensitive_ParsesCorrectly()
    {
        var columns = SchemaParser.ParseIndexColumns(
            "create index idx on t (col desc)");

        Assert.Single(columns);
        Assert.Equal("col", columns[0].Name);
        Assert.True(columns[0].IsDescending);
    }

    [Fact]
    public void ParseColumns_ExtraWhitespace_ParsesCorrectly()
    {
        var columns = SchemaParser.ParseIndexColumns(
            "CREATE  INDEX  idx  ON  t  (  a  ,  b  DESC  )");

        Assert.Equal(2, columns.Count);
        Assert.Equal("a", columns[0].Name);
        Assert.Equal("b", columns[1].Name);
        Assert.True(columns[1].IsDescending);
    }

    [Fact]
    public void ParseColumns_CollateClause_IgnoresCollate()
    {
        var columns = SchemaParser.ParseIndexColumns(
            "CREATE INDEX idx ON t (name COLLATE NOCASE)");

        Assert.Single(columns);
        Assert.Equal("name", columns[0].Name);
    }
}
