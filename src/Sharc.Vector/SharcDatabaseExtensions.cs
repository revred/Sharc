// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

namespace Sharc.Vector;

/// <summary>
/// Extension methods for vector similarity and hybrid search on Sharc databases.
/// </summary>
public static class SharcDatabaseExtensions
{
    /// <summary>
    /// Creates a vector similarity search handle for the specified table and vector column.
    /// Pre-resolves table schema, vector column ordinal, and distance metric at creation time.
    /// </summary>
    /// <param name="db">The database instance.</param>
    /// <param name="tableName">The table containing vector data.</param>
    /// <param name="vectorColumn">The BLOB column storing float vectors.</param>
    /// <param name="metric">The distance metric to use (default: Cosine).</param>
    /// <returns>A reusable <see cref="VectorQuery"/> handle.</returns>
    /// <exception cref="ArgumentException">If the vector column is not found in the table.</exception>
    /// <exception cref="InvalidOperationException">If the table is empty (cannot determine dimensions).</exception>
    public static VectorQuery Vector(this SharcDatabase db, string tableName,
        string vectorColumn, DistanceMetric metric = DistanceMetric.Cosine)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentException.ThrowIfNullOrEmpty(tableName);
        ArgumentException.ThrowIfNullOrEmpty(vectorColumn);

        var jit = db.Jit(tableName);
        try
        {
            // Validate that the vector column exists
            var table = jit.Table
                ?? throw new ArgumentException($"'{tableName}' is not a table.", nameof(tableName));

            bool columnFound = false;
            for (int i = 0; i < table.Columns.Count; i++)
            {
                if (table.Columns[i].Name.Equals(vectorColumn, StringComparison.OrdinalIgnoreCase))
                {
                    columnFound = true;
                    break;
                }
            }

            if (!columnFound)
                throw new ArgumentException(
                    $"Column '{vectorColumn}' not found in table '{tableName}'.", nameof(vectorColumn));

            // Probe first row to determine dimensions
            int dimensions;
            using (var probe = jit.Query(vectorColumn))
            {
                if (!probe.Read())
                    throw new InvalidOperationException(
                        $"Table '{tableName}' is empty — cannot determine vector dimensions.");
                dimensions = BlobVectorCodec.GetDimensions(probe.GetBlobSpan(0).Length);
            }

            return new VectorQuery(db, jit, vectorColumn, dimensions, metric);
        }
        catch
        {
            jit.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Creates a hybrid search handle combining vector similarity and text keyword relevance
    /// with Reciprocal Rank Fusion (RRF).
    /// </summary>
    /// <param name="db">The database instance.</param>
    /// <param name="tableName">The table containing both vector and text data.</param>
    /// <param name="vectorColumn">The BLOB column storing float vectors.</param>
    /// <param name="textColumn">The TEXT column for keyword matching.</param>
    /// <param name="metric">The distance metric for vector similarity (default: Cosine).</param>
    /// <returns>A reusable <see cref="HybridQuery"/> handle.</returns>
    /// <exception cref="ArgumentException">If the vector or text column is not found.</exception>
    /// <exception cref="InvalidOperationException">If the table is empty (cannot determine dimensions).</exception>
    public static HybridQuery Hybrid(this SharcDatabase db, string tableName,
        string vectorColumn, string textColumn, DistanceMetric metric = DistanceMetric.Cosine)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentException.ThrowIfNullOrEmpty(tableName);
        ArgumentException.ThrowIfNullOrEmpty(vectorColumn);
        ArgumentException.ThrowIfNullOrEmpty(textColumn);

        var vectorJit = db.Jit(tableName);
        JitQuery? textJit = null;
        try
        {
            var table = vectorJit.Table
                ?? throw new ArgumentException($"'{tableName}' is not a table.", nameof(tableName));

            bool vecFound = false, txtFound = false;
            for (int i = 0; i < table.Columns.Count; i++)
            {
                string colName = table.Columns[i].Name;
                if (colName.Equals(vectorColumn, StringComparison.OrdinalIgnoreCase)) vecFound = true;
                if (colName.Equals(textColumn, StringComparison.OrdinalIgnoreCase)) txtFound = true;
            }

            if (!vecFound)
                throw new ArgumentException(
                    $"Column '{vectorColumn}' not found in '{tableName}'.", nameof(vectorColumn));
            if (!txtFound)
                throw new ArgumentException(
                    $"Column '{textColumn}' not found in '{tableName}'.", nameof(textColumn));

            int dimensions;
            using (var probe = vectorJit.Query(vectorColumn))
            {
                if (!probe.Read())
                    throw new InvalidOperationException(
                        $"Table '{tableName}' is empty — cannot determine vector dimensions.");
                dimensions = BlobVectorCodec.GetDimensions(probe.GetBlobSpan(0).Length);
            }

            textJit = db.Jit(tableName);

            return new HybridQuery(db, vectorJit, textJit, vectorColumn, textColumn, dimensions, metric);
        }
        catch
        {
            vectorJit.Dispose();
            textJit?.Dispose();
            throw;
        }
    }
}
