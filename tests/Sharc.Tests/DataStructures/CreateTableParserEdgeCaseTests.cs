/*-------------------------------------------------------------------------------------------------!
  "Where the mind is free to imagine and the craft is guided by clarity, code awakens."            |

  A collaborative work shaped by Artificial Intelligence and curated with intent by Ram Revanur.
  Software here is treated not as static text, but as a living system designed to learn and evolve.
  Built on the belief that architecture and context often define outcomes before code is written.

  This file reflects an AI-aware, agentic, context-driven, and continuously evolving approach
  to modern engineering. If you seek to transform a traditional codebase into an adaptive,
  intelligence-guided system, you may find resonance in these patterns and principles.

  Subtle conversations often begin with a single message — or a prompt with the right context.
  https://www.linkedin.com/in/revodoc/

  Licensed under the MIT License — free for personal and commercial use.                           |
--------------------------------------------------------------------------------------------------*/

using Sharc.Core.Schema;
using Xunit;

namespace Sharc.Tests.DataStructures;

/// <summary>
/// Edge case tests for CreateTableParser.
/// Verifies handling of unusual but valid SQL patterns, malformed input, and
/// complex column constraint combinations.
/// </summary>
public class CreateTableParserEdgeCaseTests
{
    // --- Malformed or empty SQL ---

    [Fact]
    public void ParseColumns_NoParentheses_ReturnsEmpty()
    {
        var result = CreateTableParser.ParseColumns("CREATE TABLE test");
        Assert.Empty(result);
    }

    [Fact]
    public void ParseColumns_EmptyBody_ReturnsEmpty()
    {
        var result = CreateTableParser.ParseColumns("CREATE TABLE test ()");
        Assert.Empty(result);
    }

    [Fact]
    public void ParseColumns_OnlyWhitespaceInBody_ReturnsEmpty()
    {
        var result = CreateTableParser.ParseColumns("CREATE TABLE test (   )");
        Assert.Empty(result);
    }

    // --- Only table-level constraints, no columns ---

    [Fact]
    public void ParseColumns_OnlyTableConstraints_ReturnsEmpty()
    {
        var result = CreateTableParser.ParseColumns(
            "CREATE TABLE test (PRIMARY KEY (a, b))");
        Assert.Empty(result);
    }

    // --- AUTOINCREMENT keyword ---

    [Fact]
    public void ParseColumns_IntegerPrimaryKeyAutoincrement_DetectedAsPk()
    {
        var result = CreateTableParser.ParseColumns(
            "CREATE TABLE test (id INTEGER PRIMARY KEY AUTOINCREMENT, name TEXT)");

        Assert.Equal(2, result.Count);
        Assert.True(result[0].IsPrimaryKey);
        Assert.Equal("INTEGER", result[0].DeclaredType);
    }

    // --- Nested parentheses in type ---

    [Fact]
    public void ParseColumns_VarcharWithLength_IncludesParens()
    {
        var result = CreateTableParser.ParseColumns(
            "CREATE TABLE test (name VARCHAR(255), code CHAR(10))");

        Assert.Equal(2, result.Count);
        Assert.Equal("VARCHAR(255)", result[0].DeclaredType);
        Assert.Equal("CHAR(10)", result[1].DeclaredType);
    }

    [Fact]
    public void ParseColumns_DecimalWithPrecision_IncludesParens()
    {
        var result = CreateTableParser.ParseColumns(
            "CREATE TABLE test (amount DECIMAL(10,2))");

        Assert.Single(result);
        Assert.Equal("DECIMAL(10,2)", result[0].DeclaredType);
    }

    // --- Mixed quoting styles in same table ---

    [Fact]
    public void ParseColumns_MixedQuoteStyles_AllParsed()
    {
        var result = CreateTableParser.ParseColumns(
            "CREATE TABLE test (\"col1\" TEXT, [col2] INTEGER, `col3` REAL)");

        Assert.Equal(3, result.Count);
        Assert.Equal("col1", result[0].Name);
        Assert.Equal("col2", result[1].Name);
        Assert.Equal("col3", result[2].Name);
    }

    // --- Reserved words as column names (quoted) ---

    [Fact]
    public void ParseColumns_ReservedWordColumnName_ParsedCorrectly()
    {
        var result = CreateTableParser.ParseColumns(
            "CREATE TABLE test (\"select\" TEXT, [table] INTEGER, `order` REAL)");

        Assert.Equal(3, result.Count);
        Assert.Equal("select", result[0].Name);
        Assert.Equal("table", result[1].Name);
        Assert.Equal("order", result[2].Name);
    }

    // --- IF NOT EXISTS ---

    [Fact]
    public void ParseColumns_IfNotExists_StillParses()
    {
        var result = CreateTableParser.ParseColumns(
            "CREATE TABLE IF NOT EXISTS test (id INTEGER PRIMARY KEY, name TEXT)");

        Assert.Equal(2, result.Count);
        Assert.Equal("id", result[0].Name);
        Assert.Equal("name", result[1].Name);
    }

    // --- DEFAULT values ---

    [Fact]
    public void ParseColumns_DefaultValue_DoesNotBreakParsing()
    {
        var result = CreateTableParser.ParseColumns(
            "CREATE TABLE test (id INTEGER PRIMARY KEY, status TEXT DEFAULT 'active', count INTEGER DEFAULT 0)");

        Assert.Equal(3, result.Count);
        Assert.Equal("status", result[1].Name);
        Assert.Equal("count", result[2].Name);
    }

    // --- FOREIGN KEY as table constraint ---

    [Fact]
    public void ParseColumns_ForeignKeyTableConstraint_Ignored()
    {
        var result = CreateTableParser.ParseColumns(
            "CREATE TABLE orders (id INTEGER PRIMARY KEY, user_id INTEGER, FOREIGN KEY (user_id) REFERENCES users(id))");

        Assert.Equal(2, result.Count);
        Assert.Equal("id", result[0].Name);
        Assert.Equal("user_id", result[1].Name);
    }

    // --- CONSTRAINT keyword before table constraint ---

    [Fact]
    public void ParseColumns_NamedConstraint_Ignored()
    {
        var result = CreateTableParser.ParseColumns(
            "CREATE TABLE test (a INTEGER, b TEXT, CONSTRAINT pk_test PRIMARY KEY (a))");

        Assert.Equal(2, result.Count);
    }

    // --- Column with COLLATE ---

    [Fact]
    public void ParseColumns_CollateNocase_TypeStopsBeforeCollate()
    {
        var result = CreateTableParser.ParseColumns(
            "CREATE TABLE test (name TEXT COLLATE NOCASE)");

        Assert.Single(result);
        Assert.Equal("name", result[0].Name);
        Assert.Equal("TEXT", result[0].DeclaredType);
    }

    // --- Column with REFERENCES (column-level foreign key) ---

    [Fact]
    public void ParseColumns_ColumnLevelReferences_TypeStopsBeforeReferences()
    {
        var result = CreateTableParser.ParseColumns(
            "CREATE TABLE test (user_id INTEGER REFERENCES users(id))");

        Assert.Single(result);
        Assert.Equal("INTEGER", result[0].DeclaredType);
    }

    // --- No type name ---

    [Fact]
    public void ParseColumns_NoTypeName_EmptyDeclaredType()
    {
        var result = CreateTableParser.ParseColumns(
            "CREATE TABLE test (value)");

        Assert.Single(result);
        Assert.Equal("value", result[0].Name);
        Assert.Equal("", result[0].DeclaredType);
    }

    // --- Multiple NOT NULL and UNIQUE on same column ---

    [Fact]
    public void ParseColumns_NotNullAndUnique_DetectsNotNull()
    {
        var result = CreateTableParser.ParseColumns(
            "CREATE TABLE test (email TEXT NOT NULL UNIQUE)");

        Assert.Single(result);
        Assert.True(result[0].IsNotNull);
    }

    // --- Non-INTEGER PRIMARY KEY is detected as PK but not as rowid alias ---
    // TEXT PRIMARY KEY is a real primary key column, just not a rowid alias.
    // The rowid alias check in SharcDataReader gates on INTEGER type specifically.

    [Fact]
    public void ParseColumns_TextPrimaryKey_IsDetectedAsPrimaryKey()
    {
        var result = CreateTableParser.ParseColumns(
            "CREATE TABLE test (code TEXT PRIMARY KEY, name TEXT)");

        Assert.Equal(2, result.Count);
        // TEXT PRIMARY KEY IS a primary key (needed for WITHOUT ROWID support)
        Assert.True(result[0].IsPrimaryKey);
        // But its declared type is TEXT, not INTEGER — so it is NOT a rowid alias
        Assert.Equal("TEXT", result[0].DeclaredType);
    }

    // --- Ordinals are sequential ---

    [Fact]
    public void ParseColumns_FiveColumns_OrdinalsAreSequential()
    {
        var result = CreateTableParser.ParseColumns(
            "CREATE TABLE test (a INTEGER, b TEXT, c REAL, d BLOB, e INTEGER)");

        Assert.Equal(5, result.Count);
        for (int i = 0; i < 5; i++)
            Assert.Equal(i, result[i].Ordinal);
    }
}
