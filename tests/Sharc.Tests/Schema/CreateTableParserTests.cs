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

using Sharc.Core.Schema;
using Xunit;

namespace Sharc.Tests.Schema;

public class CreateTableParserTests
{
    [Fact]
    public void ParseColumns_SimpleTable_ReturnsAllColumns()
    {
        const string sql = "CREATE TABLE users (id INTEGER, name TEXT, age INTEGER)";

        var columns = CreateTableParser.ParseColumns(sql);

        Assert.Equal(3, columns.Count);
        Assert.Equal("id", columns[0].Name);
        Assert.Equal("name", columns[1].Name);
        Assert.Equal("age", columns[2].Name);
    }

    [Fact]
    public void ParseColumns_WithTypes_ReturnsDeclaredTypes()
    {
        const string sql = "CREATE TABLE items (id INTEGER, label TEXT, price REAL)";

        var columns = CreateTableParser.ParseColumns(sql);

        Assert.Equal("INTEGER", columns[0].DeclaredType);
        Assert.Equal("TEXT", columns[1].DeclaredType);
        Assert.Equal("REAL", columns[2].DeclaredType);
    }

    [Fact]
    public void ParseColumns_IntegerPrimaryKey_DetectsAsPk()
    {
        const string sql = "CREATE TABLE users (id INTEGER PRIMARY KEY, name TEXT)";

        var columns = CreateTableParser.ParseColumns(sql);

        Assert.True(columns[0].IsPrimaryKey);
        Assert.True(columns[0].IsNotNull); // PK is implicitly NOT NULL
        Assert.False(columns[1].IsPrimaryKey);
    }

    [Fact]
    public void ParseColumns_NotNullConstraint_Detected()
    {
        const string sql = "CREATE TABLE users (id INTEGER, name TEXT NOT NULL)";

        var columns = CreateTableParser.ParseColumns(sql);

        Assert.False(columns[0].IsNotNull);
        Assert.True(columns[1].IsNotNull);
    }

    [Fact]
    public void ParseColumns_CompoundType_VarChar255_ReturnsFullName()
    {
        const string sql = "CREATE TABLE items (id INTEGER, label VARCHAR(255))";

        var columns = CreateTableParser.ParseColumns(sql);

        Assert.Equal("VARCHAR(255)", columns[1].DeclaredType);
    }

    [Fact]
    public void ParseColumns_WithTableConstraints_IgnoresConstraints()
    {
        const string sql = "CREATE TABLE users (id INTEGER, name TEXT, PRIMARY KEY (id))";

        var columns = CreateTableParser.ParseColumns(sql);

        Assert.Equal(2, columns.Count);
        Assert.Equal("id", columns[0].Name);
        Assert.Equal("name", columns[1].Name);
    }

    [Fact]
    public void ParseColumns_QuotedColumnName_HandlesDoubleQuotes()
    {
        const string sql = "CREATE TABLE t (\"my column\" TEXT, normal INTEGER)";

        var columns = CreateTableParser.ParseColumns(sql);

        Assert.Equal(2, columns.Count);
        Assert.Equal("my column", columns[0].Name);
        Assert.Equal("normal", columns[1].Name);
    }

    [Fact]
    public void ParseColumns_QuotedColumnName_HandlesBrackets()
    {
        const string sql = "CREATE TABLE t ([my column] TEXT)";

        var columns = CreateTableParser.ParseColumns(sql);

        Assert.Single(columns);
        Assert.Equal("my column", columns[0].Name);
    }

    [Fact]
    public void ParseColumns_QuotedColumnName_HandlesBackticks()
    {
        const string sql = "CREATE TABLE t (`my column` TEXT)";

        var columns = CreateTableParser.ParseColumns(sql);

        Assert.Single(columns);
        Assert.Equal("my column", columns[0].Name);
    }

    [Fact]
    public void ParseColumns_OrdinalIsCorrect()
    {
        const string sql = "CREATE TABLE t (a INTEGER, b TEXT, c REAL)";

        var columns = CreateTableParser.ParseColumns(sql);

        Assert.Equal(0, columns[0].Ordinal);
        Assert.Equal(1, columns[1].Ordinal);
        Assert.Equal(2, columns[2].Ordinal);
    }

    [Fact]
    public void ParseColumns_NoType_ReturnsEmptyType()
    {
        const string sql = "CREATE TABLE t (id, name)";

        var columns = CreateTableParser.ParseColumns(sql);

        Assert.Equal(2, columns.Count);
        Assert.Equal("", columns[0].DeclaredType);
        Assert.Equal("", columns[1].DeclaredType);
    }

    [Fact]
    public void ParseColumns_IfNotExists_ParsesCorrectly()
    {
        const string sql = "CREATE TABLE IF NOT EXISTS users (id INTEGER PRIMARY KEY, name TEXT)";

        var columns = CreateTableParser.ParseColumns(sql);

        Assert.Equal(2, columns.Count);
        Assert.Equal("id", columns[0].Name);
    }

    [Fact]
    public void ParseColumns_ForeignKeyConstraint_Ignored()
    {
        const string sql = "CREATE TABLE orders (id INTEGER, user_id INTEGER, FOREIGN KEY (user_id) REFERENCES users(id))";

        var columns = CreateTableParser.ParseColumns(sql);

        Assert.Equal(2, columns.Count);
    }

    [Fact]
    public void ParseColumns_DefaultValue_TypeStopsBeforeDefault()
    {
        const string sql = "CREATE TABLE t (id INTEGER, status TEXT DEFAULT 'active')";

        var columns = CreateTableParser.ParseColumns(sql);

        Assert.Equal("TEXT", columns[1].DeclaredType);
    }
}
