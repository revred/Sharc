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

namespace Sharc.Tests.Schema;

public class CreateTableParserTests
{
    [Fact]
    public void ParseColumns_SimpleTable_ReturnsAllColumns()
    {
        const string sql = "CREATE TABLE users (id INTEGER, name TEXT, age INTEGER)";

        var columns = SchemaParser.ParseTableColumns(sql);

        Assert.Equal(3, columns.Count);
        Assert.Equal("id", columns[0].Name);
        Assert.Equal("name", columns[1].Name);
        Assert.Equal("age", columns[2].Name);
    }

    [Fact]
    public void ParseColumns_WithTypes_ReturnsDeclaredTypes()
    {
        const string sql = "CREATE TABLE items (id INTEGER, label TEXT, price REAL)";

        var columns = SchemaParser.ParseTableColumns(sql);

        Assert.Equal("INTEGER", columns[0].DeclaredType);
        Assert.Equal("TEXT", columns[1].DeclaredType);
        Assert.Equal("REAL", columns[2].DeclaredType);
    }

    [Fact]
    public void ParseColumns_IntegerPrimaryKey_DetectsAsPk()
    {
        const string sql = "CREATE TABLE users (id INTEGER PRIMARY KEY, name TEXT)";

        var columns = SchemaParser.ParseTableColumns(sql);

        Assert.True(columns[0].IsPrimaryKey);
        Assert.True(columns[0].IsNotNull); // PK is implicitly NOT NULL
        Assert.False(columns[1].IsPrimaryKey);
    }

    [Fact]
    public void ParseColumns_NotNullConstraint_Detected()
    {
        const string sql = "CREATE TABLE users (id INTEGER, name TEXT NOT NULL)";

        var columns = SchemaParser.ParseTableColumns(sql);

        Assert.False(columns[0].IsNotNull);
        Assert.True(columns[1].IsNotNull);
    }

    [Fact]
    public void ParseColumns_CompoundType_VarChar255_ReturnsFullName()
    {
        const string sql = "CREATE TABLE items (id INTEGER, label VARCHAR(255))";

        var columns = SchemaParser.ParseTableColumns(sql);

        Assert.Equal("VARCHAR(255)", columns[1].DeclaredType);
    }

    [Fact]
    public void ParseColumns_WithTableConstraints_IgnoresConstraints()
    {
        const string sql = "CREATE TABLE users (id INTEGER, name TEXT, PRIMARY KEY (id))";

        var columns = SchemaParser.ParseTableColumns(sql);

        Assert.Equal(2, columns.Count);
        Assert.Equal("id", columns[0].Name);
        Assert.Equal("name", columns[1].Name);
    }

    [Fact]
    public void ParseColumns_QuotedColumnName_HandlesDoubleQuotes()
    {
        const string sql = "CREATE TABLE t (\"my column\" TEXT, normal INTEGER)";

        var columns = SchemaParser.ParseTableColumns(sql);

        Assert.Equal(2, columns.Count);
        Assert.Equal("my column", columns[0].Name);
        Assert.Equal("normal", columns[1].Name);
    }

    [Fact]
    public void ParseColumns_QuotedColumnName_HandlesBrackets()
    {
        const string sql = "CREATE TABLE t ([my column] TEXT)";

        var columns = SchemaParser.ParseTableColumns(sql);

        Assert.Single(columns);
        Assert.Equal("my column", columns[0].Name);
    }

    [Fact]
    public void ParseColumns_QuotedColumnName_HandlesBackticks()
    {
        const string sql = "CREATE TABLE t (`my column` TEXT)";

        var columns = SchemaParser.ParseTableColumns(sql);

        Assert.Single(columns);
        Assert.Equal("my column", columns[0].Name);
    }

    [Fact]
    public void ParseColumns_OrdinalIsCorrect()
    {
        const string sql = "CREATE TABLE t (a INTEGER, b TEXT, c REAL)";

        var columns = SchemaParser.ParseTableColumns(sql);

        Assert.Equal(0, columns[0].Ordinal);
        Assert.Equal(1, columns[1].Ordinal);
        Assert.Equal(2, columns[2].Ordinal);
    }

    [Fact]
    public void ParseColumns_NoType_ReturnsEmptyType()
    {
        const string sql = "CREATE TABLE t (id, name)";

        var columns = SchemaParser.ParseTableColumns(sql);

        Assert.Equal(2, columns.Count);
        Assert.Equal("", columns[0].DeclaredType);
        Assert.Equal("", columns[1].DeclaredType);
    }

    [Fact]
    public void ParseColumns_IfNotExists_ParsesCorrectly()
    {
        const string sql = "CREATE TABLE IF NOT EXISTS users (id INTEGER PRIMARY KEY, name TEXT)";

        var columns = SchemaParser.ParseTableColumns(sql);

        Assert.Equal(2, columns.Count);
        Assert.Equal("id", columns[0].Name);
    }

    [Fact]
    public void ParseColumns_ForeignKeyConstraint_Ignored()
    {
        const string sql = "CREATE TABLE orders (id INTEGER, user_id INTEGER, FOREIGN KEY (user_id) REFERENCES users(id))";

        var columns = SchemaParser.ParseTableColumns(sql);

        Assert.Equal(2, columns.Count);
    }

    [Fact]
    public void ParseColumns_DefaultValue_TypeStopsBeforeDefault()
    {
        const string sql = "CREATE TABLE t (id INTEGER, status TEXT DEFAULT 'active')";

        var columns = SchemaParser.ParseTableColumns(sql);

        Assert.Equal("TEXT", columns[1].DeclaredType);
    }

    // --- GUID / UUID type recognition ---

    [Fact]
    public void ParseColumns_GuidType_IsGuidColumnTrue()
    {
        const string sql = "CREATE TABLE t (id INTEGER PRIMARY KEY, owner_id GUID NOT NULL)";

        var columns = SchemaParser.ParseTableColumns(sql);

        Assert.Equal(2, columns.Count);
        Assert.False(columns[0].IsGuidColumn);
        Assert.True(columns[1].IsGuidColumn);
        Assert.Equal("GUID", columns[1].DeclaredType);
    }

    [Fact]
    public void ParseColumns_UuidType_IsGuidColumnTrue()
    {
        const string sql = "CREATE TABLE t (id INTEGER, ref_id UUID)";

        var columns = SchemaParser.ParseTableColumns(sql);

        Assert.True(columns[1].IsGuidColumn);
        Assert.Equal("UUID", columns[1].DeclaredType);
    }

    [Fact]
    public void ParseColumns_GuidTypeCaseInsensitive_IsGuidColumnTrue()
    {
        const string sql = "CREATE TABLE t (id guid, ref uuid)";

        var columns = SchemaParser.ParseTableColumns(sql);

        Assert.True(columns[0].IsGuidColumn);
        Assert.True(columns[1].IsGuidColumn);
    }

    [Fact]
    public void ParseColumns_NonGuidType_IsGuidColumnFalse()
    {
        const string sql = "CREATE TABLE t (id INTEGER, name TEXT, data BLOB)";

        var columns = SchemaParser.ParseTableColumns(sql);

        Assert.False(columns[0].IsGuidColumn);
        Assert.False(columns[1].IsGuidColumn);
        Assert.False(columns[2].IsGuidColumn);
    }

    // --- MergeColumnPairs: __hi/__lo → merged GUID column ---

    [Fact]
    public void MergeColumnPairs_HiLoPair_CreatesMergedColumn()
    {
        const string sql = "CREATE TABLE t (id INTEGER PRIMARY KEY, owner__hi INTEGER, owner__lo INTEGER)";
        var physical = SchemaParser.ParseTableColumns(sql);

        var (logical, physCount) = SchemaParser.MergeColumnPairs(physical);

        Assert.Equal(3, physCount);
        Assert.Equal(2, logical.Count);
        Assert.Equal("id", logical[0].Name);
        Assert.Equal("owner", logical[1].Name);
        Assert.True(logical[1].IsMergedGuidColumn);
        Assert.True(logical[1].IsGuidColumn);
        Assert.Equal("GUID", logical[1].DeclaredType);
        Assert.Equal((int[])[1, 2], logical[1].MergedPhysicalOrdinals);
    }

    [Fact]
    public void MergeColumnPairs_NoPairs_ReturnsOriginal()
    {
        const string sql = "CREATE TABLE t (id INTEGER, name TEXT, age INTEGER)";
        var physical = SchemaParser.ParseTableColumns(sql);

        var (logical, physCount) = SchemaParser.MergeColumnPairs(physical);

        Assert.Equal(3, physCount);
        Assert.Equal(3, logical.Count);
        Assert.Equal("id", logical[0].Name);
        Assert.Equal("name", logical[1].Name);
        Assert.Equal("age", logical[2].Name);
        Assert.False(logical[0].IsMergedGuidColumn);
    }

    [Fact]
    public void MergeColumnPairs_MixedColumns_MergesOnlyPairs()
    {
        const string sql = "CREATE TABLE t (id INTEGER, guid__hi INTEGER, guid__lo INTEGER, name TEXT)";
        var physical = SchemaParser.ParseTableColumns(sql);

        var (logical, physCount) = SchemaParser.MergeColumnPairs(physical);

        Assert.Equal(4, physCount);
        Assert.Equal(3, logical.Count);
        Assert.Equal("id", logical[0].Name);
        Assert.Equal("guid", logical[1].Name);
        Assert.True(logical[1].IsMergedGuidColumn);
        Assert.Equal("name", logical[2].Name);
        Assert.False(logical[2].IsMergedGuidColumn);
    }

    [Fact]
    public void MergeColumnPairs_HiWithoutLo_NoMerge()
    {
        const string sql = "CREATE TABLE t (id INTEGER, orphan__hi INTEGER)";
        var physical = SchemaParser.ParseTableColumns(sql);

        var (logical, physCount) = SchemaParser.MergeColumnPairs(physical);

        Assert.Equal(2, physCount);
        Assert.Equal(2, logical.Count);
        Assert.Equal("orphan__hi", logical[1].Name);
        Assert.False(logical[1].IsMergedGuidColumn);
    }

    [Fact]
    public void MergeColumnPairs_LoWithoutHi_NoMerge()
    {
        const string sql = "CREATE TABLE t (id INTEGER, orphan__lo INTEGER)";
        var physical = SchemaParser.ParseTableColumns(sql);

        var (logical, physCount) = SchemaParser.MergeColumnPairs(physical);

        Assert.Equal(2, physCount);
        Assert.Equal(2, logical.Count);
        Assert.Equal("orphan__lo", logical[1].Name);
        Assert.False(logical[1].IsMergedGuidColumn);
    }

    [Fact]
    public void MergeColumnPairs_CaseInsensitive_Merges()
    {
        const string sql = "CREATE TABLE t (id INTEGER, ref__HI INTEGER, ref__LO INTEGER)";
        var physical = SchemaParser.ParseTableColumns(sql);

        var (logical, physCount) = SchemaParser.MergeColumnPairs(physical);

        Assert.Equal(3, physCount);
        Assert.Equal(2, logical.Count);
        Assert.Equal("ref", logical[1].Name);
        Assert.True(logical[1].IsMergedGuidColumn);
    }

    [Fact]
    public void MergeColumnPairs_MultiplePairs_AllMerged()
    {
        const string sql = "CREATE TABLE t (id INTEGER, a__hi INTEGER, a__lo INTEGER, b__hi INTEGER, b__lo INTEGER)";
        var physical = SchemaParser.ParseTableColumns(sql);

        var (logical, physCount) = SchemaParser.MergeColumnPairs(physical);

        Assert.Equal(5, physCount);
        Assert.Equal(3, logical.Count);
        Assert.Equal("a", logical[1].Name);
        Assert.True(logical[1].IsMergedGuidColumn);
        Assert.Equal("b", logical[2].Name);
        Assert.True(logical[2].IsMergedGuidColumn);
    }

    [Fact]
    public void MergeColumnPairs_MergedOrdinals_Sequential()
    {
        const string sql = "CREATE TABLE t (id INTEGER, x__hi INTEGER, x__lo INTEGER, name TEXT)";
        var physical = SchemaParser.ParseTableColumns(sql);

        var (logical, _) = SchemaParser.MergeColumnPairs(physical);

        Assert.Equal(0, logical[0].Ordinal);
        Assert.Equal(1, logical[1].Ordinal);
        Assert.Equal(2, logical[2].Ordinal);
    }

    [Fact]
    public void MergeColumnPairs_PhysicalColumnCount_Correct()
    {
        const string sql = "CREATE TABLE t (a INTEGER, b__hi INTEGER, b__lo INTEGER, c TEXT, d__hi INTEGER, d__lo INTEGER)";
        var physical = SchemaParser.ParseTableColumns(sql);

        var (logical, physCount) = SchemaParser.MergeColumnPairs(physical);

        Assert.Equal(6, physCount);
        Assert.Equal(4, logical.Count); // a, b(merged), c, d(merged)
    }

    [Fact]
    public void MergeColumnPairs_BothNotNull_MergedIsNotNull()
    {
        const string sql = "CREATE TABLE t (id INTEGER, g__hi INTEGER NOT NULL, g__lo INTEGER NOT NULL)";
        var physical = SchemaParser.ParseTableColumns(sql);

        var (logical, _) = SchemaParser.MergeColumnPairs(physical);

        Assert.True(logical[1].IsNotNull);
    }

    [Fact]
    public void MergeColumnPairs_OneNotNull_MergedIsNotNotNull()
    {
        const string sql = "CREATE TABLE t (id INTEGER, g__hi INTEGER NOT NULL, g__lo INTEGER)";
        var physical = SchemaParser.ParseTableColumns(sql);

        var (logical, _) = SchemaParser.MergeColumnPairs(physical);

        Assert.False(logical[1].IsNotNull);
    }
}
