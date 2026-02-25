// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

namespace Sharc;

public sealed partial class JitQuery
{
    private void ThrowIfViewBacked()
    {
        if (_view != null)
            throw new NotSupportedException(
                "JitQuery backed by a view does not support mutations. Use a table-backed JitQuery.");
    }

    private IFilterNode? CompileFilters()
    {
        var ts = _table;
        if (ts == null) return null; // view-backed - filters compiled differently

        if (_filters is null or { Count: 0 })
        {
            ts.CachedFilterNode = null;
            _compiledFilterVersion = _filterVersion;
            return null;
        }

        if (!IsFilterStale && ts.CachedFilterNode != null)
            return ts.CachedFilterNode;

        IFilterStar expression = _filters.Count == 1
            ? _filters[0]
            : FilterStar.And(_filters.ToArray());

        ts.CachedFilterNode = FilterTreeCompiler.CompileBaked(
            expression, ts.Info.Columns, ts.RowidAliasOrdinal);
        _compiledFilterVersion = _filterVersion;
        return ts.CachedFilterNode;
    }

    private int[]? ResolveProjection(string[]? columns)
    {
        if (columns is not { Length: > 0 })
            return null;

        if (_cachedProjection != null && ProjectionColumnsMatch(columns, _cachedProjectionColumns))
            return _cachedProjection;

        var projection = new int[columns.Length];
        for (int i = 0; i < columns.Length; i++)
            projection[i] = ResolveColumnOrdinal(columns[i]);

        _cachedProjection = projection;
        _cachedProjectionColumns = columns;
        return projection;
    }

    private static bool ProjectionColumnsMatch(string[]? a, string[]? b)
    {
        if (a is null || b is null || a.Length != b.Length)
            return false;
        for (int i = 0; i < a.Length; i++)
        {
            if (!a[i].Equals(b[i], StringComparison.OrdinalIgnoreCase))
                return false;
        }
        return true;
    }

    private int ResolveColumnOrdinal(string columnName)
    {
        var tableInfo = _table!.Info;
        for (int i = 0; i < tableInfo.Columns.Count; i++)
        {
            if (tableInfo.Columns[i].Name.Equals(columnName, StringComparison.OrdinalIgnoreCase))
                return tableInfo.Columns[i].Ordinal;
        }

        throw new ArgumentException($"Column '{columnName}' not found in table '{tableInfo.Name}'.");
    }
}
