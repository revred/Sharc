// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using System.Buffers.Binary;

namespace Sharc.Vector.Hnsw;

/// <summary>
/// Binary serialization for HNSW graph topology.
/// Packs the entire graph into a single byte array for storage as a BLOB.
/// </summary>
internal static class HnswSerializer
{
    private const int FormatVersion = 1;

    /// <summary>
    /// Serializes HNSW config + graph topology to a byte array.
    /// Format:
    /// [int32: version][int32: M][int32: M0][int32: efConstruction][int32: efSearch]
    /// [int32: dimensions][int32: metric][int32: entryPoint][int32: maxLevel]
    /// [int32: nodeCount][byte: useHeuristic][int32: seed]
    /// For each node:
    ///   [int64: rowId][int32: level]
    ///   For each layer 0..level:
    ///     [int32: neighborCount][int32[]: neighbors]
    /// </summary>
    internal static byte[] Serialize(HnswGraph graph, HnswConfig config,
        int dimensions, DistanceMetric metric)
    {
        // Calculate total size
        int headerSize = 4 * 10 + 1 + 4; // 10 int32s + 1 byte + 1 int32

        int bodySize = 0;
        for (int i = 0; i < graph.NodeCount; i++)
        {
            bodySize += 8; // rowId (int64)
            bodySize += 4; // level (int32)
            int level = graph.GetLevel(i);
            for (int l = 0; l <= level; l++)
            {
                var neighbors = graph.GetNeighbors(l, i);
                bodySize += 4; // neighborCount
                bodySize += neighbors.Length * 4; // neighbor indices
            }
        }

        var buffer = new byte[headerSize + bodySize];
        int pos = 0;

        // Header
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(pos), FormatVersion); pos += 4;
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(pos), config.M); pos += 4;
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(pos), config.M0); pos += 4;
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(pos), config.EfConstruction); pos += 4;
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(pos), config.EfSearch); pos += 4;
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(pos), dimensions); pos += 4;
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(pos), (int)metric); pos += 4;
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(pos), graph.EntryPoint); pos += 4;
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(pos), graph.MaxLevel); pos += 4;
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(pos), graph.NodeCount); pos += 4;
        buffer[pos++] = config.UseHeuristic ? (byte)1 : (byte)0;
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(pos), config.Seed); pos += 4;

        // Body: per-node data
        for (int i = 0; i < graph.NodeCount; i++)
        {
            BinaryPrimitives.WriteInt64LittleEndian(buffer.AsSpan(pos), graph.GetRowId(i)); pos += 8;
            int level = graph.GetLevel(i);
            BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(pos), level); pos += 4;

            for (int l = 0; l <= level; l++)
            {
                var neighbors = graph.GetNeighbors(l, i);
                BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(pos), neighbors.Length); pos += 4;
                for (int n = 0; n < neighbors.Length; n++)
                {
                    BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(pos), neighbors[n]); pos += 4;
                }
            }
        }

        return buffer;
    }

    /// <summary>
    /// Deserializes HNSW graph and config from a byte array.
    /// </summary>
    internal static (HnswGraph Graph, HnswConfig Config, int Dimensions, DistanceMetric Metric) Deserialize(
        ReadOnlySpan<byte> data)
    {
        int pos = 0;

        // Header
        int version = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(pos)); pos += 4;
        if (version != FormatVersion)
            throw new InvalidOperationException($"Unsupported HNSW format version: {version}");

        int m = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(pos)); pos += 4;
        int m0 = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(pos)); pos += 4;
        int efConstruction = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(pos)); pos += 4;
        int efSearch = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(pos)); pos += 4;
        int dimensions = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(pos)); pos += 4;
        int metricInt = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(pos)); pos += 4;
        int entryPoint = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(pos)); pos += 4;
        int maxLevel = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(pos)); pos += 4;
        int nodeCount = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(pos)); pos += 4;
        bool useHeuristic = data[pos++] != 0;
        int seed = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(pos)); pos += 4;

        var config = new HnswConfig
        {
            M = m,
            M0 = m0,
            EfConstruction = efConstruction,
            EfSearch = efSearch,
            UseHeuristic = useHeuristic,
            Seed = seed
        };

        var metric = (DistanceMetric)metricInt;
        var graph = new HnswGraph(nodeCount, maxLevel);
        graph.EntryPoint = entryPoint;
        graph.MaxLevel = maxLevel;

        // Body: per-node data
        for (int i = 0; i < nodeCount; i++)
        {
            long rowId = BinaryPrimitives.ReadInt64LittleEndian(data.Slice(pos)); pos += 8;
            int level = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(pos)); pos += 4;

            graph.SetRowId(i, rowId);
            graph.SetLevel(i, level);

            for (int l = 0; l <= level; l++)
            {
                int neighborCount = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(pos)); pos += 4;
                var neighbors = new int[neighborCount];
                for (int n = 0; n < neighborCount; n++)
                {
                    neighbors[n] = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(pos)); pos += 4;
                }
                graph.SetNeighbors(l, i, neighbors);
            }
        }

        return (graph, config, dimensions, metric);
    }
}
