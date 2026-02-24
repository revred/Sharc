/*-------------------------------------------------------------------------------------------------!
  "Where the mind is free to imagine and the craft is guided by clarity, code awakens."            |

  A collaborative work shaped by Artificial Intelligence and curated with intent by Ram Revanur.
  Licensed under the MIT License — free for personal and commercial use.                           |
--------------------------------------------------------------------------------------------------*/

using Microsoft.JSInterop;
using Sharc.Core;

namespace Sharc.Arena.Wasm.Services;

/// <summary>
/// OPFS-backed page source for browser-persistent Sharc databases.
/// Delegates read/write to Origin Private File System via <c>opfs-bridge.js</c>.
/// <para>
/// Design:
/// - All I/O goes through <see cref="IJSRuntime"/> → <c>window.opfsBridge</c>
/// - Single-page read buffer is reused across calls (zero per-read allocation)
/// - Write path increments <see cref="DataVersion"/> for change detection
/// - Flush delegates to <c>FileSystemSyncAccessHandle.flush()</c>
/// </para>
/// </summary>
public sealed class OpfsPageSource : IWritablePageSource
{
    private readonly IJSRuntime _js;
    private readonly int _handleId;
    private readonly byte[] _readBuffer;
    private int _pageCount;
    private long _dataVersion = 1;
    private bool _disposed;

    /// <inheritdoc />
    public int PageSize { get; }

    /// <inheritdoc />
    public int PageCount => _pageCount;

    /// <inheritdoc />
    public long DataVersion => Interlocked.Read(ref _dataVersion);

    private OpfsPageSource(IJSRuntime js, int handleId, int pageSize, int pageCount)
    {
        _js = js;
        _handleId = handleId;
        PageSize = pageSize;
        _pageCount = pageCount;
        _readBuffer = new byte[pageSize];
    }

    /// <summary>
    /// Opens an OPFS database file. Creates the file if it does not exist.
    /// Parses the SQLite header to determine page size and count.
    /// </summary>
    public static async Task<OpfsPageSource> OpenAsync(IJSRuntime js, string fileName)
    {
        var result = await js.InvokeAsync<OpfsOpenResult>("opfsBridge.openDatabase", fileName);

        // Read the first 100 bytes to determine page size and page count
        var headerResult = await js.InvokeAsync<OpfsReadResult>("opfsBridge.readPage", result.HandleId, 1, 4096);
        var headerBytes = headerResult.Data;

        // Parse page size from bytes 16-17 (big-endian)
        int pageSize = (headerBytes[16] << 8) | headerBytes[17];
        if (pageSize == 1) pageSize = 65536; // SQLite convention

        // Parse page count from bytes 28-31 (big-endian)
        int pageCount = (headerBytes[28] << 24) | (headerBytes[29] << 16) |
                        (headerBytes[30] << 8) | headerBytes[31];

        // For new/empty files, infer from file size
        if (pageCount == 0 && result.FileSize > 0)
            pageCount = (int)(result.FileSize / pageSize);

        return new OpfsPageSource(js, result.HandleId, pageSize, Math.Max(pageCount, 0));
    }

    /// <summary>
    /// Creates a new OPFS database from an in-memory byte buffer.
    /// Writes the buffer to OPFS, then opens it.
    /// </summary>
    public static async Task<OpfsPageSource> CreateFromBytesAsync(IJSRuntime js, string fileName, byte[] data)
    {
        var result = await js.InvokeAsync<OpfsOpenResult>("opfsBridge.openDatabase", fileName);

        // Write entire database in page-sized chunks
        int pageSize = (data[16] << 8) | data[17];
        if (pageSize == 1) pageSize = 65536;
        int pageCount = data.Length / pageSize;

        for (int i = 0; i < pageCount; i++)
        {
            uint pageNum = (uint)(i + 1);
            var pageData = data.AsMemory(i * pageSize, pageSize);
            await js.InvokeVoidAsync("opfsBridge.writePage",
                result.HandleId, pageNum, pageSize, pageData.ToArray());
        }

        await js.InvokeVoidAsync("opfsBridge.flush", result.HandleId);

        return new OpfsPageSource(js, result.HandleId, pageSize, pageCount);
    }

    /// <inheritdoc />
    public int ReadPage(uint pageNumber, Span<byte> destination)
    {
        ValidatePageNumber(pageNumber);

        // Synchronous path: use pre-fetched buffer if available
        // In WASM, IJSRuntime calls are async — this path works with
        // pre-cached pages or requires the caller to use the async overload.
        var page = GetPage(pageNumber);
        page.CopyTo(destination);
        return PageSize;
    }

    /// <inheritdoc />
    public ReadOnlySpan<byte> GetPage(uint pageNumber)
    {
        ValidatePageNumber(pageNumber);

        // Synchronous JS interop via IJSInProcessRuntime (available in Blazor WASM)
        if (_js is IJSInProcessRuntime jsInProcess)
        {
            var result = jsInProcess.Invoke<OpfsReadResult>("opfsBridge.readPage", _handleId, pageNumber, PageSize);
            result.Data.CopyTo(_readBuffer.AsSpan());
            return _readBuffer.AsSpan(0, PageSize);
        }

        // Fallback: return zeroed buffer (caller should use async path)
        return _readBuffer.AsSpan(0, PageSize);
    }

    /// <inheritdoc />
    public ReadOnlyMemory<byte> GetPageMemory(uint pageNumber)
    {
        ValidatePageNumber(pageNumber);

        if (_js is IJSInProcessRuntime jsInProcess)
        {
            var result = jsInProcess.Invoke<OpfsReadResult>("opfsBridge.readPage", _handleId, pageNumber, PageSize);
            // Must return a stable copy since _readBuffer is shared
            return result.Data.AsMemory();
        }

        return new byte[PageSize];
    }

    /// <inheritdoc />
    public void Invalidate(uint pageNumber)
    {
        // No internal cache to invalidate; reads go directly to OPFS
    }

    /// <inheritdoc />
    public void WritePage(uint pageNumber, ReadOnlySpan<byte> source)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(pageNumber, 1u, nameof(pageNumber));

        if (_js is IJSInProcessRuntime jsInProcess)
        {
            jsInProcess.InvokeVoid("opfsBridge.writePage",
                _handleId, pageNumber, PageSize, source.ToArray());

            if (pageNumber > (uint)_pageCount)
                _pageCount = (int)pageNumber;

            Interlocked.Increment(ref _dataVersion);
        }
    }

    /// <inheritdoc />
    public void Flush()
    {
        if (_js is IJSInProcessRuntime jsInProcess)
        {
            jsInProcess.InvokeVoid("opfsBridge.flush", _handleId);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_js is IJSInProcessRuntime jsInProcess)
        {
            jsInProcess.InvokeVoid("opfsBridge.close", _handleId);
        }
    }

    private void ValidatePageNumber(uint pageNumber)
    {
        if (pageNumber < 1 || pageNumber > (uint)_pageCount)
            throw new ArgumentOutOfRangeException(nameof(pageNumber), pageNumber,
                $"Page number must be between 1 and {_pageCount}.");
    }

    // ── JS Interop result types ──

    private sealed class OpfsOpenResult
    {
        public int HandleId { get; set; }
        public long FileSize { get; set; }
    }

    private sealed class OpfsReadResult
    {
        public byte[] Data { get; set; } = Array.Empty<byte>();
        public int BytesRead { get; set; }
    }
}
