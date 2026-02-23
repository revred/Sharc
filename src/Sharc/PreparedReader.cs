// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Core;

namespace Sharc;

/// <summary>
/// A pre-resolved reader handle that eliminates schema lookup, cursor creation,
/// and ArrayPool allocation on repeated Seek/Read calls. Created via
/// <see cref="SharcDatabase.PrepareReader(string, string[])"/>.
/// </summary>
/// <remarks>
/// <para>After the first <see cref="CreateReader"/> call, the cursor and reader are cached.
/// Subsequent calls reset traversal state via <see cref="IBTreeCursor.Reset"/> and reuse
/// the same buffers, eliminating per-call allocation and setup overhead.</para>
/// <para>This type is <b>not thread-safe</b>. Each instance should be used from a single thread.</para>
/// </remarks>
public sealed class PreparedReader : IDisposable
{
    private IBTreeCursor? _cursor;
    private SharcDataReader? _reader;

    internal PreparedReader(IBTreeCursor cursor, SharcDataReader reader)
    {
        _cursor = cursor;
        _reader = reader;
        reader.MarkReusable();
    }

    /// <summary>
    /// Returns the cached reader, reset for a new Seek/Read pass.
    /// Zero allocation after the first call.
    /// </summary>
    /// <returns>The cached <see cref="SharcDataReader"/> ready for Seek or Read.</returns>
    /// <exception cref="ObjectDisposedException">The prepared reader has been disposed.</exception>
    public SharcDataReader CreateReader()
    {
        ObjectDisposedException.ThrowIf(_reader is null, this);
        _reader.ResetForReuse(null);
        return _reader;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_reader != null)
        {
            _reader.DisposeForReal();
            _reader = null;
        }
        if (_cursor != null)
        {
            _cursor.Dispose();
            _cursor = null;
        }
    }
}
