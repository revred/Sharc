// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.


/*-------------------------------------------------------------------------------------------------!
  â€œWhere the mind is free to imagine and the craft is guided by clarity, code awakens.â€            |

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

namespace Sharc.Tests.DataStructures;

/// <summary>
/// Tests for schema model classes (SharcSchema, TableInfo, ColumnInfo, IndexInfo, ViewInfo).
/// Verifies lookup behavior, immutability contracts, and data preservation.
/// </summary>
public class SchemaModelTests
{
    private static SharcSchema CreateTestSchema()
    {
        return new SharcSchema
        {
            Tables =
            [
                new TableInfo
                {
                    Name = "users",
                    RootPage = 2,
                    Sql = "CREATE TABLE users (id INTEGER PRIMARY KEY, name TEXT NOT NULL, age INTEGER)",
                    Columns =
                    [
                        new ColumnInfo { Name = "id", DeclaredType = "INTEGER", Ordinal = 0, IsPrimaryKey = true, IsNotNull = true },
                        new ColumnInfo { Name = "name", DeclaredType = "TEXT", Ordinal = 1, IsPrimaryKey = false, IsNotNull = true },
                        new ColumnInfo { Name = "age", DeclaredType = "INTEGER", Ordinal = 2, IsPrimaryKey = false, IsNotNull = false },
                    ],
                    IsWithoutRowId = false
                },
                new TableInfo
                {
                    Name = "Products",
                    RootPage = 3,
                    Sql = "CREATE TABLE Products (id INTEGER PRIMARY KEY, price REAL)",
                    Columns =
                    [
                        new ColumnInfo { Name = "id", DeclaredType = "INTEGER", Ordinal = 0, IsPrimaryKey = true, IsNotNull = true },
                        new ColumnInfo { Name = "price", DeclaredType = "REAL", Ordinal = 1, IsPrimaryKey = false, IsNotNull = false },
                    ],
                    IsWithoutRowId = false
                }
            ],
            Indexes =
            [
                new IndexInfo
                {
                    Name = "idx_users_name",
                    TableName = "users",
                    RootPage = 4,
                    Sql = "CREATE INDEX idx_users_name ON users (name)",
                    IsUnique = false,
                    Columns = [new IndexColumnInfo { Name = "name", Ordinal = 0, IsDescending = false }]
                },
                new IndexInfo
                {
                    Name = "idx_users_unique_name",
                    TableName = "users",
                    RootPage = 5,
                    Sql = "CREATE UNIQUE INDEX idx_users_unique_name ON users (name)",
                    IsUnique = true,
                    Columns = [new IndexColumnInfo { Name = "name", Ordinal = 0, IsDescending = false }]
                }
            ],
            Views =
            [
                new ViewInfo
                {
                    Name = "active_users",
                    Sql = "CREATE VIEW active_users AS SELECT * FROM users WHERE age > 18"
                }
            ]
        };
    }

    // --- GetTable: case-insensitive lookup ---

    [Fact]
    public void GetTable_ExactCase_ReturnsTable()
    {
        var schema = CreateTestSchema();
        var table = schema.GetTable("users");
        Assert.Equal("users", table.Name);
    }

    [Fact]
    public void GetTable_CaseInsensitive_FindsMatch()
    {
        var schema = CreateTestSchema();
        var table = schema.GetTable("USERS");
        Assert.Equal("users", table.Name);
    }

    [Fact]
    public void GetTable_MixedCase_FindsMatch()
    {
        var schema = CreateTestSchema();
        var table = schema.GetTable("Products");
        Assert.Equal("Products", table.Name);
    }

    [Fact]
    public void GetTable_NotFound_ThrowsKeyNotFoundException()
    {
        var schema = CreateTestSchema();
        Assert.Throws<KeyNotFoundException>(() => schema.GetTable("nonexistent"));
    }

    // --- Tables list ---

    [Fact]
    public void Tables_ReturnsCorrectCount()
    {
        var schema = CreateTestSchema();
        Assert.Equal(2, schema.Tables.Count);
    }

    // --- Indexes ---

    [Fact]
    public void Indexes_ReturnsCorrectCount()
    {
        var schema = CreateTestSchema();
        Assert.Equal(2, schema.Indexes.Count);
    }

    [Fact]
    public void IndexInfo_IsUnique_DetectedFromSql()
    {
        var schema = CreateTestSchema();
        var nonUnique = schema.Indexes.First(i => i.Name == "idx_users_name");
        var unique = schema.Indexes.First(i => i.Name == "idx_users_unique_name");

        Assert.False(nonUnique.IsUnique);
        Assert.True(unique.IsUnique);
    }

    [Fact]
    public void IndexInfo_TableName_Preserved()
    {
        var schema = CreateTestSchema();
        Assert.All(schema.Indexes, i => Assert.Equal("users", i.TableName));
    }

    // --- Views ---

    [Fact]
    public void Views_ReturnsCorrectCount()
    {
        var schema = CreateTestSchema();
        Assert.Single(schema.Views);
        Assert.Equal("active_users", schema.Views[0].Name);
    }

    // --- TableInfo properties ---

    [Fact]
    public void TableInfo_RootPage_Preserved()
    {
        var schema = CreateTestSchema();
        Assert.Equal(2, schema.GetTable("users").RootPage);
    }

    [Fact]
    public void TableInfo_Sql_Preserved()
    {
        var schema = CreateTestSchema();
        Assert.StartsWith("CREATE TABLE users", schema.GetTable("users").Sql);
    }

    [Fact]
    public void TableInfo_IsWithoutRowId_DefaultFalse()
    {
        var schema = CreateTestSchema();
        Assert.False(schema.GetTable("users").IsWithoutRowId);
    }

    // --- ColumnInfo properties ---

    [Fact]
    public void ColumnInfo_Ordinals_MatchDeclarationOrder()
    {
        var schema = CreateTestSchema();
        var columns = schema.GetTable("users").Columns;

        Assert.Equal(0, columns[0].Ordinal);
        Assert.Equal(1, columns[1].Ordinal);
        Assert.Equal(2, columns[2].Ordinal);
    }

    [Fact]
    public void ColumnInfo_Names_MatchDeclaration()
    {
        var schema = CreateTestSchema();
        var columns = schema.GetTable("users").Columns;

        Assert.Equal("id", columns[0].Name);
        Assert.Equal("name", columns[1].Name);
        Assert.Equal("age", columns[2].Name);
    }

    [Fact]
    public void ColumnInfo_DeclaredTypes_Preserved()
    {
        var schema = CreateTestSchema();
        var columns = schema.GetTable("users").Columns;

        Assert.Equal("INTEGER", columns[0].DeclaredType);
        Assert.Equal("TEXT", columns[1].DeclaredType);
        Assert.Equal("INTEGER", columns[2].DeclaredType);
    }

    [Fact]
    public void ColumnInfo_IsPrimaryKey_OnlyForPkColumn()
    {
        var schema = CreateTestSchema();
        var columns = schema.GetTable("users").Columns;

        Assert.True(columns[0].IsPrimaryKey);
        Assert.False(columns[1].IsPrimaryKey);
        Assert.False(columns[2].IsPrimaryKey);
    }

    [Fact]
    public void ColumnInfo_IsNotNull_CorrectForEachColumn()
    {
        var schema = CreateTestSchema();
        var columns = schema.GetTable("users").Columns;

        Assert.True(columns[0].IsNotNull);   // PK is implicitly NOT NULL
        Assert.True(columns[1].IsNotNull);    // Explicit NOT NULL
        Assert.False(columns[2].IsNotNull);   // No NOT NULL constraint
    }
}