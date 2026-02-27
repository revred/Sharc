// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

namespace Sharc.Vector.Hnsw;

/// <summary>
/// Core in-memory HNSW graph structure. Stores topology (neighbor lists per layer)
/// using dense integer node indices for cache-friendly access. Vectors are never stored
/// in the graph — they are resolved on-demand via <see cref="IVectorResolver"/>.
/// </summary>
/// <remarks>
/// Supports post-build growth via <see cref="AddNode"/> for incremental HNSW insertion.
/// Internal arrays are resized with 2x growth factor when capacity is exhausted.
/// </remarks>
internal sealed class HnswGraph
{
    // nodeIndex → rowId mapping (dense array, length = _capacity)
    private long[] _nodeRowIds;

    // rowId → nodeIndex reverse lookup
    private readonly Dictionary<long, int> _rowIdToIndex;

    // nodeIndex → max layer assigned to this node (length = _capacity)
    private int[] _nodeLevels;

    // [layer][nodeIndex] → neighbor indices (null if node not present at layer)
    // Outer length = _layerCapacity; inner length = _capacity per layer
    private int[]?[][] _neighbors;

    private int _entryPoint;
    private int _maxLevel;
    private int _nodeCount;
    private int _capacity;

    internal HnswGraph(int nodeCount, int maxLayers)
    {
        _nodeCount = nodeCount;
        _capacity = nodeCount;
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

    /// <summary>Tries to get the node index for a rowId.</summary>
    internal bool TryGetNodeIndex(long rowId, out int nodeIndex)
        => _rowIdToIndex.TryGetValue(rowId, out nodeIndex);

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

    /// <summary>
    /// Adds a new node to the graph, growing internal arrays as needed.
    /// Returns the dense node index assigned to the new node.
    /// </summary>
    /// <param name="rowId">Row ID of the new vector.</param>
    /// <param name="level">HNSW layer assignment for this node.</param>
    internal int AddNode(long rowId, int level)
    {
        if (_rowIdToIndex.ContainsKey(rowId))
            throw new InvalidOperationException(
                $"Row ID {rowId} already exists in the graph at node index {_rowIdToIndex[rowId]}. " +
                $"Use UpdateRowVector for existing rows.");

        if (level < 0)
            throw new ArgumentOutOfRangeException(nameof(level), level, "Level must be non-negative.");

        int newIndex = _nodeCount;

        // Grow capacity if needed (2x growth factor)
        if (newIndex >= _capacity)
        {
            int newCapacity = Math.Max(_capacity * 2, 8);
            GrowArrays(newCapacity);
        }

        // Ensure enough layers exist
        if (level >= _neighbors.Length)
        {
            int newLayerCount = level + 1;
            var newLayers = new int[]?[newLayerCount][];
            Array.Copy(_neighbors, newLayers, _neighbors.Length);
            for (int l = _neighbors.Length; l < newLayerCount; l++)
                newLayers[l] = new int[]?[_capacity];
            _neighbors = newLayers;
        }

        _nodeRowIds[newIndex] = rowId;
        _rowIdToIndex[rowId] = newIndex;
        _nodeLevels[newIndex] = level;
        _nodeCount++;

        return newIndex;
    }

    /// <summary>
    /// Updates the vector for an existing node by rowId. Only modifies the
    /// reverse-lookup dictionary entry (no topology change). The caller is
    /// responsible for updating the <see cref="IVectorResolver"/>.
    /// Returns the node index, or -1 if not found.
    /// </summary>
    internal int UpdateRowVector(long rowId)
    {
        if (_rowIdToIndex.TryGetValue(rowId, out int idx))
            return idx;
        return -1;
    }

    private void GrowArrays(int newCapacity)
    {
        var newRowIds = new long[newCapacity];
        Array.Copy(_nodeRowIds, newRowIds, _nodeCount);
        _nodeRowIds = newRowIds;

        var newLevels = new int[newCapacity];
        Array.Copy(_nodeLevels, newLevels, _nodeCount);
        _nodeLevels = newLevels;

        for (int l = 0; l < _neighbors.Length; l++)
        {
            var newLayer = new int[]?[newCapacity];
            Array.Copy(_neighbors[l], newLayer, _nodeCount);
            _neighbors[l] = newLayer;
        }

        _capacity = newCapacity;
    }
}
