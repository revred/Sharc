// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

namespace Sharc.Views;

/// <summary>
/// Forward-only cursor over a view's projected rows.
/// Extends <see cref="IRowAccessor"/> for column access — ordinals are
/// remapped to the view's projection. Zero allocation per row.
/// </summary>
public interface IViewCursor : IRowAccessor, IDisposable
{
    /// <summary>Advance to the next row. Returns false when exhausted.</summary>
    bool MoveNext();

    /// <summary>Number of rows read so far.</summary>
    long RowsRead { get; }
}
