// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

namespace Sharc.Vector.Hnsw;

/// <summary>
/// Core in-memory HNSW graph structure. Stores topology (neighbor lists per layer)
/// using dense integer node indices for cache-friendly access. Vectors are never stored
/// in the graph — they are resolved on-demand via <see cref="IVectorResolver"/>.
/// </summary>
internal sealed class HnswGraph
{
    // nodeIndex → rowId mapping (dense array)
    private long[] _nodeRowIds;

    // rowId → nodeIndex reverse lookup
    private readonly Dictionary<long, int> _rowIdToIndex;

    // nodeIndex → max layer assigned to this node
    private readonly int[] _nodeLevels;

    // [layer][nodeIndex] → neighbor indices (null if node not present at layer)
    private readonly int[]?[][] _neighbors;

    private int _entryPoint;
    private int _maxLevel;
    private readonly int _nodeCount;

    internal HnswGraph(int nodeCount, int maxLayers)
    {
        _nodeCount = nodeCount;
        _nodeRowIds = new long[nodeCount];
        _rowIdToIndex = new Dictionary<long, int>(nodeCount);
        _nodeLevels = new int[nodeCount];
        _neighbors = new int[]?[maxLayers + 1][];
        for (int l = 0; l <= maxLayers; l++)
            _neighbors[l] = new int[]?[nodeCount];
        _entryPoint = -1;
        _maxLevel = -1;
    }

    /// <summary>Total number of nodes in the graph.</summary>
    internal int NodeCount => _nodeCount;

    /// <summary>The maximum layer index (0-based).</summary>
    internal int MaxLevel
    {
        get => _maxLevel;
        set => _maxLevel = value;
    }

    /// <summary>The entry point node index.</summary>
    internal int EntryPoint
    {
        get => _entryPoint;
        set => _entryPoint = value;
    }

    /// <summary>Gets the rowId for a node index.</summary>
    internal long GetRowId(int nodeIndex) => _nodeRowIds[nodeIndex];

    /// <summary>Gets the node index for a rowId.</summary>
    internal int GetNodeIndex(long rowId) => _rowIdToIndex[rowId];

    /// <summary>Sets the rowId for a node index.</summary>
    internal void SetRowId(int nodeIndex, long rowId)
    {
        _nodeRowIds[nodeIndex] = rowId;
        _rowIdToIndex[rowId] = nodeIndex;
    }

    /// <summary>Gets the max layer assigned to this node.</summary>
    internal int GetLevel(int nodeIndex) => _nodeLevels[nodeIndex];

    /// <summary>Sets the max layer for a node.</summary>
    internal void SetLevel(int nodeIndex, int level) => _nodeLevels[nodeIndex] = level;

    /// <summary>Gets the neighbor list for a node at a given layer.</summary>
    internal ReadOnlySpan<int> GetNeighbors(int layer, int nodeIndex)
    {
        var arr = _neighbors[layer][nodeIndex];
        return arr ?? ReadOnlySpan<int>.Empty;
    }

    /// <summary>Gets the neighbor array (mutable) for internal builder use.</summary>
    internal int[]? GetNeighborArray(int layer, int nodeIndex)
        => _neighbors[layer][nodeIndex];

    /// <summary>Sets the neighbor list for a node at a given layer.</summary>
    internal void SetNeighbors(int layer, int nodeIndex, int[] neighbors)
        => _neighbors[layer][nodeIndex] = neighbors;

    /// <summary>Number of allocated layers.</summary>
    internal int LayerCount => _neighbors.Length;

    /// <summary>Gets the raw rowId array (for serialization).</summary>
    internal long[] NodeRowIds => _nodeRowIds;

    /// <summary>Gets the raw level array (for serialization).</summary>
    internal int[] NodeLevels => _nodeLevels;

    /// <summary>Gets the raw neighbor array for a layer (for serialization).</summary>
    internal int[]?[] GetLayerNeighbors(int layer) => _neighbors[layer];
}
