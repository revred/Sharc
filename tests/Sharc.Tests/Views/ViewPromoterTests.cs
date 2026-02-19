// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Core.Schema;
using Sharc.Views;
using Xunit;

namespace Sharc.Tests.Views;

public sealed class ViewPromoterTests
{
    private static SharcSchema CreateSchema()
    {
        return new SharcSchema
        {
            Tables =
            [
                new TableInfo
                {
                    Name = "users",
                    RootPage = 2,
                    Sql = "CREATE TABLE users (id INTEGER PRIMARY KEY, name TEXT, age INTEGER)",
                    IsWithoutRowId = false,
                    Columns =
                    [
                        new ColumnInfo { Name = "id", DeclaredType = "INTEGER", Ordinal = 0, IsPrimaryKey = true, IsNotNull = false },
                        new ColumnInfo { Name = "name", DeclaredType = "TEXT", Ordinal = 1, IsPrimaryKey = false, IsNotNull = false },
                        new ColumnInfo { Name = "age", DeclaredType = "INTEGER", Ordinal = 2, IsPrimaryKey = false, IsNotNull = false }
                    ]
                }
            ],
            Indexes = [],
            Views = []
        };
    }

    [Fact]
    public void TryPromote_SimpleSelectAll_ReturnsView()
    {
        var schema = CreateSchema();
        var viewInfo = new ViewInfo
        {
            Name = "v_all",
            Sql = "CREATE VIEW v_all AS SELECT * FROM users",
            SourceTables = ["users"],
            Columns = [],
            IsSelectAll = true,
            HasJoin = false,
            HasFilter = false,
            ParseSucceeded = true
        };

        var view = ViewPromoter.TryPromote(viewInfo, schema);

        Assert.NotNull(view);
        Assert.Equal("v_all", view!.Name);
        Assert.Equal("users", view.SourceTable);
        Assert.Null(view.ProjectedColumnNames); // SELECT * = no projection
        Assert.Null(view.Filter);
    }

    [Fact]
    public void TryPromote_ProjectedColumns_ReturnsViewWithProjection()
    {
        var schema = CreateSchema();
        var viewInfo = new ViewInfo
        {
            Name = "v_names",
            Sql = "CREATE VIEW v_names AS SELECT name, age FROM users",
            SourceTables = ["users"],
            Columns =
            [
                new ViewColumnInfo { SourceName = "name", DisplayName = "name", Ordinal = 0 },
                new ViewColumnInfo { SourceName = "age", DisplayName = "age", Ordinal = 1 }
            ],
            IsSelectAll = false,
            HasJoin = false,
            HasFilter = false,
            ParseSucceeded = true
        };

        var view = ViewPromoter.TryPromote(viewInfo, schema);

        Assert.NotNull(view);
        Assert.Equal("v_names", view!.Name);
        Assert.NotNull(view.ProjectedColumnNames);
        Assert.Equal(2, view.ProjectedColumnNames!.Count);
        Assert.Equal("name", view.ProjectedColumnNames[0]);
        Assert.Equal("age", view.ProjectedColumnNames[1]);
    }

    [Fact]
    public void TryPromote_WithJoin_ReturnsNull()
    {
        var schema = CreateSchema();
        var viewInfo = new ViewInfo
        {
            Name = "v_joined",
            Sql = "CREATE VIEW v_joined AS SELECT * FROM users JOIN orders ON ...",
            SourceTables = ["users", "orders"],
            Columns = [],
            IsSelectAll = true,
            HasJoin = true,
            HasFilter = false,
            ParseSucceeded = true
        };

        var view = ViewPromoter.TryPromote(viewInfo, schema);

        Assert.Null(view);
    }

    [Fact]
    public void TryPromote_WithFilter_ReturnsNull()
    {
        var schema = CreateSchema();
        var viewInfo = new ViewInfo
        {
            Name = "v_filtered",
            Sql = "CREATE VIEW v_filtered AS SELECT * FROM users WHERE age > 18",
            SourceTables = ["users"],
            Columns = [],
            IsSelectAll = true,
            HasJoin = false,
            HasFilter = true,
            ParseSucceeded = true
        };

        var view = ViewPromoter.TryPromote(viewInfo, schema);

        Assert.Null(view);
    }

    [Fact]
    public void TryPromote_ParseFailed_ReturnsNull()
    {
        var schema = CreateSchema();
        var viewInfo = new ViewInfo
        {
            Name = "v_bad",
            Sql = "GARBAGE SQL",
            ParseSucceeded = false
        };

        var view = ViewPromoter.TryPromote(viewInfo, schema);

        Assert.Null(view);
    }

    [Fact]
    public void TryPromote_UnknownTable_ReturnsNull()
    {
        var schema = CreateSchema();
        var viewInfo = new ViewInfo
        {
            Name = "v_unknown",
            Sql = "CREATE VIEW v_unknown AS SELECT * FROM nonexistent",
            SourceTables = ["nonexistent"],
            Columns = [],
            IsSelectAll = true,
            HasJoin = false,
            HasFilter = false,
            ParseSucceeded = true
        };

        var view = ViewPromoter.TryPromote(viewInfo, schema);

        Assert.Null(view);
    }
}
