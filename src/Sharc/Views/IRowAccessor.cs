// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

namespace Sharc.Views;

/// <summary>
/// Provides typed, zero-allocation column access by ordinal.
/// Implemented by view cursors and data readers.
/// </summary>
public interface IRowAccessor
{
    /// <summary>Gets the number of columns.</summary>
    int FieldCount { get; }

    /// <summary>Gets a column value as a 64-bit signed integer.</summary>
    long GetInt64(int ordinal);

    /// <summary>Gets a column value as a double-precision float.</summary>
    double GetDouble(int ordinal);

    /// <summary>Gets a column value as a UTF-8 string.</summary>
    string GetString(int ordinal);

    /// <summary>Gets a column value as a byte array (BLOB).</summary>
    byte[] GetBlob(int ordinal);

    /// <summary>Returns true if the column value is NULL.</summary>
    bool IsNull(int ordinal);

    /// <summary>Gets the column name at the specified ordinal.</summary>
    string GetColumnName(int ordinal);

    /// <summary>Gets the SQLite storage class for the column at the current row.</summary>
    SharcColumnType GetColumnType(int ordinal);
}
