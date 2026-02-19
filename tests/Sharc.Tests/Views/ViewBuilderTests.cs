// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Views;
using Xunit;

namespace Sharc.Tests.Views;

public sealed class ViewBuilderTests
{
    [Fact]
    public void From_SetsSourceTable()
    {
        var view = ViewBuilder.From("users").Build();
        Assert.Equal("users", view.SourceTable);
    }

    [Fact]
    public void Named_SetsName()
    {
        var view = ViewBuilder.From("users").Named("my_view").Build();
        Assert.Equal("my_view", view.Name);
    }

    [Fact]
    public void DefaultName_IsTableName()
    {
        var view = ViewBuilder.From("users").Build();
        Assert.Equal("users", view.Name);
    }

    [Fact]
    public void Select_SetsProjectedColumns()
    {
        var view = ViewBuilder
            .From("users")
            .Select("name", "age")
            .Build();

        Assert.NotNull(view.ProjectedColumnNames);
        Assert.Equal(2, view.ProjectedColumnNames!.Count);
        Assert.Equal("name", view.ProjectedColumnNames[0]);
        Assert.Equal("age", view.ProjectedColumnNames[1]);
    }

    [Fact]
    public void Select_Empty_SetsProjectionNull()
    {
        var view = ViewBuilder
            .From("users")
            .Select()
            .Build();

        Assert.Null(view.ProjectedColumnNames);
    }

    [Fact]
    public void Where_SetsFilter()
    {
        Func<IRowAccessor, bool> filter = row => row.GetInt64(0) > 5;
        var view = ViewBuilder
            .From("users")
            .Where(filter)
            .Build();

        Assert.NotNull(view.Filter);
        Assert.Equal(filter, view.Filter);
    }

    [Fact]
    public void NoFilter_FilterIsNull()
    {
        var view = ViewBuilder.From("users").Build();
        Assert.Null(view.Filter);
    }

    [Fact]
    public void FluentChaining_AllOptions()
    {
        var view = ViewBuilder
            .From("users")
            .Select("name", "email")
            .Where(row => !row.IsNull(0))
            .Named("active_users")
            .Build();

        Assert.Equal("active_users", view.Name);
        Assert.Equal("users", view.SourceTable);
        Assert.Equal(2, view.ProjectedColumnNames!.Count);
        Assert.NotNull(view.Filter);
    }

    [Fact]
    public void From_NullTableName_Throws()
    {
        Assert.ThrowsAny<ArgumentException>(() => ViewBuilder.From((string)null!));
    }

    [Fact]
    public void From_EmptyTableName_Throws()
    {
        Assert.Throws<ArgumentException>(() => ViewBuilder.From(""));
    }

    [Fact]
    public void Named_NullName_Throws()
    {
        Assert.ThrowsAny<ArgumentException>(() => ViewBuilder.From("users").Named(null!));
    }
}
