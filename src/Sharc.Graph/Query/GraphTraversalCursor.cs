// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Graph.Model;
using Sharc.Views;

namespace Sharc.Graph.Query;

/// <summary>
/// An <see cref="IViewCursor"/> that iterates over an in-memory list of
/// <see cref="TraversalNode"/> results. Fixed 6-column schema:
/// key (INTEGER), kind (INTEGER), alias (TEXT), data (TEXT), tokens (INTEGER), depth (INTEGER).
/// </summary>
internal sealed class GraphTraversalCursor : IViewCursor
{
    private static readonly string[] ColumnNames = ["key", "kind", "alias", "data", "tokens", "depth"];

    private readonly IReadOnlyList<TraversalNode> _nodes;
    private int _index = -1;

    public GraphTraversalCursor(IReadOnlyList<TraversalNode> nodes)
    {
        _nodes = nodes ?? throw new ArgumentNullException(nameof(nodes));
    }

    public int FieldCount => 6;

    public long RowsRead => _index + 1;

    public bool MoveNext()
    {
        _index++;
        return _index < _nodes.Count;
    }

    private TraversalNode Current => _nodes[_index];

    public long GetInt64(int ordinal) => ordinal switch
    {
        0 => Current.Record.Key.Value,
        1 => Current.Record.TypeId,
        4 => Current.Record.Tokens,
        5 => Current.Depth,
        _ => 0
    };

    public double GetDouble(int ordinal) => GetInt64(ordinal);

    public string GetString(int ordinal) => ordinal switch
    {
        2 => Current.Record.Alias ?? "",
        3 => Current.Record.JsonData,
        _ => GetInt64(ordinal).ToString(System.Globalization.CultureInfo.InvariantCulture)
    };

    public byte[] GetBlob(int ordinal) => Array.Empty<byte>();

    public bool IsNull(int ordinal) => ordinal switch
    {
        2 => Current.Record.Alias == null,
        _ => false
    };

    public string GetColumnName(int ordinal) => ColumnNames[ordinal];

    public SharcColumnType GetColumnType(int ordinal) => ordinal switch
    {
        0 => SharcColumnType.Integral,
        1 => SharcColumnType.Integral,
        2 => SharcColumnType.Text,
        3 => SharcColumnType.Text,
        4 => SharcColumnType.Integral,
        5 => SharcColumnType.Integral,
        _ => SharcColumnType.Null
    };

    public void Dispose() { }
}
