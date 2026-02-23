// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

namespace Sharc;

/// <summary>
/// Common interface for all prepared reader handles that produce
/// <see cref="SharcDataReader"/> instances with pre-resolved schema,
/// cursor, and filter state.
/// </summary>
/// <remarks>
/// <para>Implementations:
/// <list type="bullet">
/// <item><see cref="PreparedReader"/> — direct table access with Seek/Read</item>
/// <item><see cref="PreparedQuery"/> — compiled SQL query with optional parameters</item>
/// </list>
/// </para>
/// <para>All implementations cache cursor and reader state after the first call.
/// Subsequent calls reset traversal state and reuse buffers for zero-allocation
/// steady-state execution.</para>
/// </remarks>
public interface IPreparedReader : IDisposable
{
    /// <summary>
    /// Returns a <see cref="SharcDataReader"/> positioned before the first row.
    /// On the first call, creates internal cursor and reader state.
    /// On subsequent calls, reuses cached state with zero allocation.
    /// </summary>
    /// <returns>A reusable <see cref="SharcDataReader"/>.</returns>
    /// <exception cref="ObjectDisposedException">The prepared reader has been disposed.</exception>
    SharcDataReader Execute();
}
