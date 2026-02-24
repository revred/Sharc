// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using System.Text;
using Sharc.Core;

namespace Sharc.Vector;

/// <summary>
/// Extension methods for inserting vector data through <see cref="SharcWriter"/>.
/// </summary>
public static class SharcWriterExtensions
{
    // Per-table dimension cache to avoid repeated probing.
    [ThreadStatic]
    private static Dictionary<string, int>? t_dimensionCache;

    /// <summary>
    /// Inserts a row with a vector BLOB column and optional metadata columns.
    /// Validates vector dimensions against existing rows in the table.
    /// </summary>
    /// <param name="writer">The writer instance.</param>
    /// <param name="tableName">Target table name.</param>
    /// <param name="vectorColumn">The BLOB column that stores the vector.</param>
    /// <param name="vector">The float vector to encode and insert.</param>
    /// <param name="metadata">Additional column name-value pairs to insert alongside the vector.</param>
    /// <returns>The row ID of the inserted row.</returns>
    /// <exception cref="ArgumentException">If the vector is empty or has mismatched dimensions.</exception>
    public static long InsertVector(this SharcWriter writer, string tableName, string vectorColumn,
        float[] vector, params (string ColumnName, object? Value)[] metadata)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentException.ThrowIfNullOrEmpty(tableName);
        ArgumentException.ThrowIfNullOrEmpty(vectorColumn);
        ArgumentNullException.ThrowIfNull(vector);

        if (vector.Length == 0)
            throw new ArgumentException("Vector must have at least one dimension.", nameof(vector));

        // Validate dimensions
        ValidateDimensions(writer, tableName, vectorColumn, vector.Length);

        // Resolve table schema to map column names → ordinals
        var table = writer.Database.Schema.Tables
            .FirstOrDefault(t => t.Name.Equals(tableName, StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException($"Table '{tableName}' not found.", nameof(tableName));

        var columns = table.Columns;

        // Build ColumnValue[] in declaration order, skipping INTEGER PRIMARY KEY (rowid alias)
        // The first column (typically "id") may be the rowid alias — if so, don't include it
        // in the values array since SharcWriter auto-assigns rowid.
        int startCol = 0;
        long? explicitRowId = null;

        // Check if first column is INTEGER PRIMARY KEY (rowid alias)
        if (columns.Count > 0 && columns[0].DeclaredType.Contains("INTEGER", StringComparison.OrdinalIgnoreCase)
            && columns[0].IsPrimaryKey)
        {
            // Check if caller provided explicit id
            for (int m = 0; m < metadata.Length; m++)
            {
                if (metadata[m].ColumnName.Equals(columns[0].Name, StringComparison.OrdinalIgnoreCase)
                    && metadata[m].Value != null)
                {
                    explicitRowId = Convert.ToInt64(metadata[m].Value, System.Globalization.CultureInfo.InvariantCulture);
                    break;
                }
            }
            startCol = 1; // Skip rowid column in ColumnValue array
        }

        int valueCount = columns.Count - startCol;
        var values = new ColumnValue[valueCount];

        // Initialize all to NULL
        for (int i = 0; i < valueCount; i++)
            values[i] = ColumnValue.Null();

        // Map metadata and vector into correct ordinals
        for (int c = startCol; c < columns.Count; c++)
        {
            int vi = c - startCol;
            string colName = columns[c].Name;

            if (colName.Equals(vectorColumn, StringComparison.OrdinalIgnoreCase))
            {
                byte[] encoded = BlobVectorCodec.Encode(vector);
                int serialType = 12 + encoded.Length * 2; // SQLite BLOB serial type
                values[vi] = ColumnValue.Blob(serialType, encoded);
                continue;
            }

            // Find in metadata
            for (int m = 0; m < metadata.Length; m++)
            {
                if (metadata[m].ColumnName.Equals(colName, StringComparison.OrdinalIgnoreCase))
                {
                    values[vi] = ToColumnValue(metadata[m].Value);
                    break;
                }
            }
        }

        return writer.Insert(tableName, values);
    }

    private static ColumnValue ToColumnValue(object? value) => value switch
    {
        null => ColumnValue.Null(),
        long l => ColumnValue.FromInt64(IntSerialType(l), l),
        int i => ColumnValue.FromInt64(IntSerialType(i), i),
        double d => ColumnValue.FromDouble(d),
        float f => ColumnValue.FromDouble(f),
        string s => TextColumnValue(s),
        byte[] b => ColumnValue.Blob(12 + b.Length * 2, b),
        _ => TextColumnValue(value.ToString() ?? string.Empty)
    };

    private static long IntSerialType(long v) => v switch
    {
        0 => 8, // zero
        1 => 9, // one
        >= -128 and <= 127 => 1,
        >= -32768 and <= 32767 => 2,
        >= -8388608 and <= 8388607 => 3,
        >= -2147483648 and <= 2147483647 => 4,
        _ => 6
    };

    private static ColumnValue TextColumnValue(string s)
    {
        byte[] utf8 = Encoding.UTF8.GetBytes(s);
        int serialType = 13 + utf8.Length * 2; // SQLite TEXT serial type
        return ColumnValue.Text(serialType, utf8);
    }

    private static void ValidateDimensions(SharcWriter writer, string tableName,
        string vectorColumn, int newDimensions)
    {
        t_dimensionCache ??= new Dictionary<string, int>();
        string cacheKey = $"{tableName}:{vectorColumn}";

        if (t_dimensionCache.TryGetValue(cacheKey, out int existingDim))
        {
            if (existingDim != newDimensions)
                throw new ArgumentException(
                    $"Vector dimension mismatch: table expects {existingDim} but got {newDimensions}.");
            return;
        }

        // Probe the table for existing dimensions
        int? probedDim = ProbeExistingDimensions(writer, tableName, vectorColumn);
        if (probedDim.HasValue)
        {
            t_dimensionCache[cacheKey] = probedDim.Value;
            if (probedDim.Value != newDimensions)
                throw new ArgumentException(
                    $"Vector dimension mismatch: table expects {probedDim.Value} but got {newDimensions}.");
        }
        else
        {
            // First insert — accept any dimension and cache it
            t_dimensionCache[cacheKey] = newDimensions;
        }
    }

    private static int? ProbeExistingDimensions(SharcWriter writer, string tableName, string vectorColumn)
    {
        using var reader = writer.Database.CreateReader(tableName, vectorColumn);
        if (reader.Read())
        {
            var blob = reader.GetBlobSpan(0);
            return BlobVectorCodec.GetDimensions(blob.Length);
        }
        return null;
    }
}
