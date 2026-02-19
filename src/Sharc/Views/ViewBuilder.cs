// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

namespace Sharc.Views;

/// <summary>
/// Fluent builder for defining <see cref="SharcView"/> instances.
/// A view can source from a table or from another view (subview).
/// <example>
/// <code>
/// // Table-sourced view
/// var view = ViewBuilder
///     .From("users")
///     .Select("name", "age")
///     .Where(row => row.GetInt64(1) > 18)
///     .Named("adults")
///     .Build();
///
/// // View-sourced subview (same type, same features)
/// var sub = ViewBuilder
///     .From(view)
///     .Select("name")
///     .Named("adult_names")
///     .Build();
/// </code>
/// </example>
/// </summary>
public sealed class ViewBuilder
{
    private readonly string? _sourceTable;
    private readonly SharcView? _sourceView;
    private string[]? _columns;
    private Func<IRowAccessor, bool>? _filter;
    private string _name;

    private ViewBuilder(string sourceTable)
    {
        _sourceTable = sourceTable;
        _name = sourceTable; // default name = table name
    }

    private ViewBuilder(SharcView sourceView)
    {
        _sourceView = sourceView;
        _name = sourceView.Name; // default name = parent view name
    }

    /// <summary>Start building a view over the specified table.</summary>
    public static ViewBuilder From(string tableName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);
        return new ViewBuilder(tableName);
    }

    /// <summary>Start building a view over another view (subview).</summary>
    public static ViewBuilder From(SharcView parentView)
    {
        ArgumentNullException.ThrowIfNull(parentView);
        return new ViewBuilder(parentView);
    }

    /// <summary>Project specific columns by name.</summary>
    /// <exception cref="ArgumentException">A column name is null or whitespace.</exception>
    public ViewBuilder Select(params string[] columnNames)
    {
        for (int i = 0; i < columnNames.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(columnNames[i]))
                throw new ArgumentException($"Column name at index {i} must not be null or whitespace.", nameof(columnNames));
        }
        _columns = columnNames.Length > 0 ? columnNames : null;
        return this;
    }

    /// <summary>Add a row filter predicate.</summary>
    public ViewBuilder Where(Func<IRowAccessor, bool> predicate)
    {
        _filter = predicate;
        return this;
    }

    /// <summary>Name the view.</summary>
    public ViewBuilder Named(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        _name = name;
        return this;
    }

    /// <summary>Build the immutable view definition.</summary>
    public SharcView Build()
    {
        if (_sourceView != null)
            return new SharcView(_name, _sourceView, _columns, _filter);

        return new SharcView(_name, _sourceTable!, _columns, _filter);
    }
}
