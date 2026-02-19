// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Query.Sharq;
using Xunit;

namespace Sharc.Query.Tests;

public sealed class ViewSqlScannerTests
{
    [Fact]
    public void Scan_SelectStarFromTable_ReturnsSelectAll()
    {
        var result = ViewSqlScanner.Scan("CREATE VIEW v AS SELECT * FROM t");

        Assert.True(result.ParseSucceeded);
        Assert.True(result.IsSelectAll);
        Assert.Single(result.SourceTables);
        Assert.Equal("t", result.SourceTables[0]);
        Assert.Empty(result.Columns);
    }

    [Fact]
    public void Scan_ExplicitColumns_ReturnsColumnsWithOrdinals()
    {
        var result = ViewSqlScanner.Scan("CREATE VIEW v AS SELECT a, b, c FROM t");

        Assert.True(result.ParseSucceeded);
        Assert.False(result.IsSelectAll);
        Assert.Equal(3, result.Columns.Length);
        Assert.Equal("a", result.Columns[0].SourceName);
        Assert.Equal(0, result.Columns[0].Ordinal);
        Assert.Equal("b", result.Columns[1].SourceName);
        Assert.Equal(1, result.Columns[1].Ordinal);
        Assert.Equal("c", result.Columns[2].SourceName);
        Assert.Equal(2, result.Columns[2].Ordinal);
        Assert.Single(result.SourceTables);
        Assert.Equal("t", result.SourceTables[0]);
    }

    [Fact]
    public void Scan_ColumnAlias_CapturesAlias()
    {
        var result = ViewSqlScanner.Scan("CREATE VIEW v AS SELECT a AS x FROM t");

        Assert.True(result.ParseSucceeded);
        Assert.Single(result.Columns);
        Assert.Equal("a", result.Columns[0].SourceName);
        Assert.Equal("x", result.Columns[0].DisplayName);
        Assert.Equal(0, result.Columns[0].Ordinal);
    }

    [Fact]
    public void Scan_MultipleAliases_CapturesAll()
    {
        var result = ViewSqlScanner.Scan("CREATE VIEW v AS SELECT a AS x, b AS y FROM t");

        Assert.True(result.ParseSucceeded);
        Assert.Equal(2, result.Columns.Length);
        Assert.Equal("a", result.Columns[0].SourceName);
        Assert.Equal("x", result.Columns[0].DisplayName);
        Assert.Equal("b", result.Columns[1].SourceName);
        Assert.Equal("y", result.Columns[1].DisplayName);
    }

    [Fact]
    public void Scan_WithWhereClause_SetsHasFilter()
    {
        var result = ViewSqlScanner.Scan("CREATE VIEW v AS SELECT a FROM t WHERE a > 5");

        Assert.True(result.ParseSucceeded);
        Assert.True(result.HasFilter);
        Assert.False(result.HasJoin);
        Assert.Single(result.SourceTables);
        Assert.Equal("t", result.SourceTables[0]);
    }

    [Fact]
    public void Scan_WithJoin_SetsHasJoinAndMultipleTables()
    {
        var result = ViewSqlScanner.Scan(
            "CREATE VIEW v AS SELECT a, b FROM t1 JOIN t2 ON t1.id = t2.fk");

        Assert.True(result.ParseSucceeded);
        Assert.True(result.HasJoin);
        Assert.Equal(2, result.SourceTables.Length);
        Assert.Equal("t1", result.SourceTables[0]);
        Assert.Equal("t2", result.SourceTables[1]);
    }

    [Fact]
    public void Scan_LeftJoin_SetsHasJoinAndMultipleTables()
    {
        var result = ViewSqlScanner.Scan(
            "CREATE VIEW v AS SELECT u.name, o.amount FROM users u LEFT JOIN orders o ON u.id = o.user_id");

        Assert.True(result.ParseSucceeded);
        Assert.True(result.HasJoin);
        Assert.Equal(2, result.SourceTables.Length);
        Assert.Equal("users", result.SourceTables[0]);
        Assert.Equal("orders", result.SourceTables[1]);
    }

    [Fact]
    public void Scan_QualifiedColumns_ExtractsColumnName()
    {
        var result = ViewSqlScanner.Scan(
            "CREATE VIEW v AS SELECT u.name, o.amount FROM users u JOIN orders o ON u.id = o.user_id");

        Assert.True(result.ParseSucceeded);
        Assert.Equal(2, result.Columns.Length);
        Assert.Equal("name", result.Columns[0].SourceName);
        Assert.Equal("amount", result.Columns[1].SourceName);
    }

    [Fact]
    public void Scan_QuotedIdentifiers_UnquotesCorrectly()
    {
        // SharqTokenizer handles quoted identifiers by stripping quotes
        var result = ViewSqlScanner.Scan(
            "CREATE VIEW v AS SELECT \"col name\" FROM [table name]");

        Assert.True(result.ParseSucceeded);
        Assert.Single(result.Columns);
        Assert.Equal("col name", result.Columns[0].SourceName);
        Assert.Single(result.SourceTables);
        Assert.Equal("table name", result.SourceTables[0]);
    }

    [Fact]
    public void Scan_CreateTempView_SkipsTempKeyword()
    {
        var result = ViewSqlScanner.Scan("CREATE TEMP VIEW v AS SELECT * FROM t");

        Assert.True(result.ParseSucceeded);
        Assert.True(result.IsSelectAll);
        Assert.Single(result.SourceTables);
        Assert.Equal("t", result.SourceTables[0]);
    }

    [Fact]
    public void Scan_CreateTemporaryView_SkipsTemporaryKeyword()
    {
        var result = ViewSqlScanner.Scan("CREATE TEMPORARY VIEW v AS SELECT a FROM t");

        Assert.True(result.ParseSucceeded);
        Assert.Single(result.Columns);
        Assert.Equal("a", result.Columns[0].SourceName);
    }

    [Fact]
    public void Scan_IfNotExists_SkipsKeywords()
    {
        var result = ViewSqlScanner.Scan(
            "CREATE VIEW IF NOT EXISTS v AS SELECT * FROM t");

        Assert.True(result.ParseSucceeded);
        Assert.True(result.IsSelectAll);
        Assert.Single(result.SourceTables);
        Assert.Equal("t", result.SourceTables[0]);
    }

    [Fact]
    public void Scan_EmptySql_ReturnsFailed()
    {
        var result = ViewSqlScanner.Scan("");

        Assert.False(result.ParseSucceeded);
    }

    [Fact]
    public void Scan_NullSql_ReturnsFailed()
    {
        var result = ViewSqlScanner.Scan(null!);

        Assert.False(result.ParseSucceeded);
    }

    [Fact]
    public void Scan_MalformedSql_ReturnsFailed()
    {
        var result = ViewSqlScanner.Scan("NOT A VIEW STATEMENT");

        Assert.False(result.ParseSucceeded);
    }

    [Fact]
    public void Scan_JoinWithWhereClause_SetsBothFlags()
    {
        var result = ViewSqlScanner.Scan(
            "CREATE VIEW v AS SELECT a FROM t1 JOIN t2 ON t1.id = t2.fk WHERE t1.a > 5");

        Assert.True(result.ParseSucceeded);
        Assert.True(result.HasJoin);
        Assert.True(result.HasFilter);
    }

    [Fact]
    public void Scan_CrossJoinViaComma_ExtractsMultipleTables()
    {
        var result = ViewSqlScanner.Scan(
            "CREATE VIEW v AS SELECT * FROM t1, t2");

        Assert.True(result.ParseSucceeded);
        Assert.Equal(2, result.SourceTables.Length);
        Assert.Equal("t1", result.SourceTables[0]);
        Assert.Equal("t2", result.SourceTables[1]);
    }

    [Fact]
    public void Scan_SelectDistinct_ParsesCorrectly()
    {
        var result = ViewSqlScanner.Scan(
            "CREATE VIEW v AS SELECT DISTINCT a, b FROM t");

        Assert.True(result.ParseSucceeded);
        Assert.Equal(2, result.Columns.Length);
        Assert.Equal("a", result.Columns[0].SourceName);
        Assert.Equal("b", result.Columns[1].SourceName);
    }

    [Fact]
    public void Scan_NoFilter_HasFilterIsFalse()
    {
        var result = ViewSqlScanner.Scan("CREATE VIEW v AS SELECT a FROM t");

        Assert.True(result.ParseSucceeded);
        Assert.False(result.HasFilter);
        Assert.False(result.HasJoin);
    }

    [Fact]
    public void Scan_SimpleView_ColumnOrdinals()
    {
        var result = ViewSqlScanner.Scan(
            "CREATE VIEW v AS SELECT name, age, email FROM users");

        Assert.True(result.ParseSucceeded);
        Assert.Equal(3, result.Columns.Length);
        Assert.Equal(0, result.Columns[0].Ordinal);
        Assert.Equal(1, result.Columns[1].Ordinal);
        Assert.Equal(2, result.Columns[2].Ordinal);
    }

    [Fact]
    public void Scan_ColumnWithoutAlias_DisplayNameEqualsSourceName()
    {
        var result = ViewSqlScanner.Scan("CREATE VIEW v AS SELECT name FROM users");

        Assert.True(result.ParseSucceeded);
        Assert.Single(result.Columns);
        Assert.Equal("name", result.Columns[0].SourceName);
        Assert.Equal("name", result.Columns[0].DisplayName);
    }
}
