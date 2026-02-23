// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Core;

namespace Sharc;

/// <summary>
/// Common interface for all prepared writer handles that perform
/// pre-resolved Insert, Delete, and Update operations on a single table.
/// </summary>
/// <remarks>
/// <para>Implementations:
/// <list type="bullet">
/// <item><see cref="PreparedWriter"/> — direct table writes with pre-resolved schema</item>
/// </list>
/// </para>
/// <para>All implementations cache <see cref="Core.Schema.TableInfo"/> and root page lookups
/// at construction time, eliminating per-call schema scans and root page resolution.</para>
/// </remarks>
public interface IPreparedWriter : IDisposable
{
    /// <summary>
    /// Inserts a record into the prepared table. Auto-commits.
    /// </summary>
    /// <param name="values">Column values for the new row.</param>
    /// <returns>The assigned rowid.</returns>
    /// <exception cref="ObjectDisposedException">The prepared writer has been disposed.</exception>
    long Insert(params ColumnValue[] values);

    /// <summary>
    /// Deletes a record by rowid from the prepared table. Auto-commits.
    /// </summary>
    /// <param name="rowId">The rowid of the row to delete.</param>
    /// <returns>True if the row existed and was removed.</returns>
    /// <exception cref="ObjectDisposedException">The prepared writer has been disposed.</exception>
    bool Delete(long rowId);

    /// <summary>
    /// Updates a record by rowid in the prepared table. Auto-commits.
    /// </summary>
    /// <param name="rowId">The rowid of the row to update.</param>
    /// <param name="values">New column values for the row.</param>
    /// <returns>True if the row existed and was updated.</returns>
    /// <exception cref="ObjectDisposedException">The prepared writer has been disposed.</exception>
    bool Update(long rowId, params ColumnValue[] values);
}
