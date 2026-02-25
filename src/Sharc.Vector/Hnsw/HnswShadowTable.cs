// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Core;

namespace Sharc.Vector.Hnsw;

/// <summary>
/// Manages the shadow table for HNSW index persistence.
/// Shadow table stores the serialized graph as a single BLOB row.
/// </summary>
/// <remarks>
/// Schema: <c>_hnsw_{sourceTable}_{vectorColumn} (id INTEGER PRIMARY KEY, graph_data BLOB)</c>
/// Single row (id=1) holds the entire serialized graph.
/// </remarks>
internal static class HnswShadowTable
{
    /// <summary>Gets the shadow table name for a source table + vector column.</summary>
    internal static string GetTableName(string sourceTable, string vectorColumn)
        => $"_hnsw_{sourceTable}_{vectorColumn}";

    /// <summary>Checks whether the shadow table exists.</summary>
    internal static bool Exists(SharcDatabase db, string shadowTableName)
    {
        var tables = db.Schema.Tables;
        for (int i = 0; i < tables.Count; i++)
        {
            if (tables[i].Name.Equals(shadowTableName, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Saves an HNSW index to the named shadow table.
    /// Uses SharcWriter for transactional DDL+DML.
    /// </summary>
    internal static void Save(SharcDatabase db, string shadowTableName,
        HnswGraph graph, HnswConfig config, int dimensions, DistanceMetric metric)
    {
        byte[] serialized = HnswSerializer.Serialize(graph, config, dimensions, metric);

        using var writer = SharcWriter.From(db);

        if (Exists(db, shadowTableName))
        {
            // Delete existing data and re-insert
            using var jit = db.Jit(shadowTableName);
            using var reader = jit.Query();
            while (reader.Read())
                writer.Delete(shadowTableName, reader.RowId);
        }
        else
        {
            // Create the shadow table via DDL transaction
            using var ddlTx = db.BeginTransaction();
            string createSql = $"CREATE TABLE [{shadowTableName}] (id INTEGER PRIMARY KEY, graph_data BLOB)";
            ddlTx.Execute(createSql);
            ddlTx.Commit();
        }

        // Insert the serialized graph as a single row
        int blobSerialType = 12 + serialized.Length * 2;
        var values = new ColumnValue[]
        {
            ColumnValue.Null(), // id (rowid alias — auto-assigned)
            ColumnValue.Blob(blobSerialType, serialized)
        };

        writer.Insert(shadowTableName, values);
    }

    /// <summary>
    /// Loads an HNSW index from the shadow table.
    /// </summary>
    /// <returns>The deserialized graph, config, dimensions, and metric; or null if not found.</returns>
    internal static (HnswGraph Graph, HnswConfig Config, int Dimensions, DistanceMetric Metric)? Load(
        SharcDatabase db, string shadowTableName)
    {
        if (!Exists(db, shadowTableName))
            return null;

        using var jit = db.Jit(shadowTableName);
        using var reader = jit.Query("graph_data");
        if (!reader.Read())
            return null;

        var blob = reader.GetBlobSpan(0);
        return HnswSerializer.Deserialize(blob);
    }
}
