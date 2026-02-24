// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Graph.Model;

namespace Sharc.Graph.Tests.Unit.Algorithms;

/// <summary>
/// Lightweight fake IEdgeCursor backed by an in-memory list of edges.
/// Used for pure algorithm tests without requiring MultiTableFakeReader.
/// </summary>
internal sealed class FakeEdgeCursor : IEdgeCursor
{
    private readonly List<(long Target, int Kind, float Weight)> _edges;
    private readonly long _originKey;
    private int _index = -1;

    public FakeEdgeCursor(long originKey, List<(long Target, int Kind, float Weight)>? edges)
    {
        _originKey = originKey;
        _edges = edges ?? [];
    }

    public long OriginKey => _originKey;
    public long TargetKey => _index >= 0 && _index < _edges.Count ? _edges[_index].Target : 0;
    public int Kind => _index >= 0 && _index < _edges.Count ? _edges[_index].Kind : 0;
    public float Weight => _index >= 0 && _index < _edges.Count ? _edges[_index].Weight : 0f;
    public ReadOnlyMemory<byte> JsonDataUtf8 => ReadOnlyMemory<byte>.Empty;

    public bool MoveNext()
    {
        _index++;
        return _index < _edges.Count;
    }

    public void Reset(long matchKey, int? matchKind = null)
    {
        _index = -1;
    }

    public void Dispose() { }
}

/// <summary>
/// Helper that builds adjacency lists and produces IEdgeCursor instances on demand.
/// </summary>
internal sealed class FakeGraphBuilder
{
    private readonly Dictionary<long, List<(long Target, int Kind, float Weight)>> _outgoing = new();
    private readonly Dictionary<long, List<(long Target, int Kind, float Weight)>> _incoming = new();

    public FakeGraphBuilder AddEdge(long from, long to, int kind = 10, float weight = 1.0f)
    {
        if (!_outgoing.TryGetValue(from, out var outList))
        {
            outList = [];
            _outgoing[from] = outList;
        }
        outList.Add((to, kind, weight));

        if (!_incoming.TryGetValue(to, out var inList))
        {
            inList = [];
            _incoming[to] = inList;
        }
        inList.Add((from, kind, weight));

        return this;
    }

    public IEdgeCursor CreateOutgoing(NodeKey key) =>
        new FakeEdgeCursor(key.Value, _outgoing.GetValueOrDefault(key.Value));

    public IEdgeCursor CreateIncoming(NodeKey key) =>
        new FakeEdgeCursor(key.Value, _incoming.GetValueOrDefault(key.Value));

    public IReadOnlyList<NodeKey> Nodes
    {
        get
        {
            var keys = new HashSet<long>();
            foreach (var k in _outgoing.Keys) keys.Add(k);
            foreach (var k in _incoming.Keys) keys.Add(k);
            return keys.Select(k => new NodeKey(k)).ToList();
        }
    }
}
