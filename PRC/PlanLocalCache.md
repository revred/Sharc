# PlanLocalCache — Browser-Persistent Sharc Databases

**Status**: Proposed
**Priority**: High — Most requested feature gap
**Target**: v1.2.0

---

## Problem Statement

Today, Sharc databases in Blazor WASM exist only in memory. Every page refresh destroys the database. Users cannot:

1. Upload a `.db` file and keep it between sessions
2. Pre-cache a database for offline access
3. Resume benchmarks after a browser refresh
4. Build apps that persist data client-side without a server

This is the **single biggest adoption blocker** for Blazor WASM scenarios. The ~40 KB engine footprint advantage is wasted if every page load requires a full re-download or regeneration of the database.

---

## Current Architecture

```
User uploads/generates .db
        ↓
    byte[] in RAM (DataGenerator or HttpClient.GetByteArrayAsync)
        ↓
    MemoryPageSource (zero-copy span slices)
        ↓
    SharcDatabase.OpenMemory(bytes)
        ↓
    Page refresh → ALL DATA LOST
```

**Key constraint**: `MemoryPageSource` operates on a contiguous `byte[]`. There is no page-level I/O abstraction for browser storage.

**Existing IndexedDB usage** (Arena only): The `indexeddb-adapter.js` stores *rows as JSON objects* — not SQLite pages. This is a benchmarking shim, not a storage layer.

---

## Proposed Architecture

### Core Concept: `IndexedDbPageSource : IPageSource`

Store SQLite pages (4 KB each) as binary blobs in IndexedDB, keyed by page number. This gives Sharc a **virtual file system** in the browser.

```
User uploads/generates .db
        ↓
    byte[] split into 4 KB pages
        ↓
    IndexedDB object store (key: pageNumber, value: Uint8Array)
        ↓
    IndexedDbPageSource (JS interop per page read)
        ↓
    CachedPageSource (LRU cache eliminates repeated interop)
        ↓
    SharcDatabase opens from IndexedDbPageSource
        ↓
    Page refresh → DATA PERSISTS in IndexedDB
```

### Why Page-Level (Not Row-Level)

| Approach | Pros | Cons |
| :--- | :--- | :--- |
| **Page-level blobs** | Format-preserving, simple, works with all Sharc features (encryption, WAL, graph) | Requires JS interop per cache miss |
| **Row-level JSON** | Human-readable in DevTools | Loses SQLite format, can't use BTreeCursor, breaks encryption, massive overhead |
| **Full-file blob** | Simplest implementation | 100 MB+ databases hit IndexedDB quota fast, no partial reads |

**Decision: Page-level blobs.** This preserves the entire Sharc stack (B-tree, encryption, graph) while enabling partial reads and efficient quota usage.

---

## Implementation Plan

### Phase 1: IndexedDbPageSource (Core)

**Goal**: Implement `IPageSource` backed by IndexedDB via JS interop.

**New files**:
- `src/Sharc.Core/IO/IndexedDbPageSource.cs` — The page source implementation
- `src/Sharc/Wasm/IndexedDbStorageAdapter.cs` — C# wrapper for JS interop
- `wwwroot/js/sharc-page-store.js` — JS module for IndexedDB page operations

**IndexedDB Schema**:
```javascript
// Database: "sharc-pages"
// Object store: "pages-{databaseId}"
//   Key: page number (uint32)
//   Value: Uint8Array (page data, typically 4096 bytes)

// Object store: "metadata"
//   Key: databaseId (string)
//   Value: { pageSize, pageCount, createdAt, lastAccessedAt, sizeBytes, name }
```

**C# Interface**:
```csharp
public sealed class IndexedDbPageSource : IPageSource, IAsyncDisposable
{
    // Factory — async because IndexedDB open is async
    public static async Task<IndexedDbPageSource> OpenAsync(
        IJSRuntime js, string databaseId, CancellationToken ct = default);

    // IPageSource — synchronous reads via cached pages
    public ReadOnlySpan<byte> GetPage(uint pageNumber);

    // Bulk import — write an entire .db file into IndexedDB
    public static async Task ImportAsync(
        IJSRuntime js, string databaseId, byte[] dbBytes, CancellationToken ct = default);

    // Bulk export — read all pages back to byte[]
    public async Task<byte[]> ExportAsync(CancellationToken ct = default);
}
```

**JS Module** (`sharc-page-store.js`):
```javascript
export async function openDatabase(databaseId) { ... }
export async function readPage(databaseId, pageNumber) { ... }
export async function readPages(databaseId, pageNumbers) { ... }  // batch
export async function writePage(databaseId, pageNumber, data) { ... }
export async function writePages(databaseId, pages) { ... }  // batch import
export async function getMetadata(databaseId) { ... }
export async function deleteDatabase(databaseId) { ... }
export async function listDatabases() { ... }
```

**Key Design Decisions**:

1. **Sync GetPage via pre-warm**: IndexedDB is async-only. Solution: on open, pre-read the first N pages (header + schema + root pages) synchronously via a pre-warm buffer. Remaining pages use `CachedPageSource` with async background fetch.

2. **Batch import**: When importing a `.db` file, write all pages in a single IndexedDB transaction for atomicity and performance.

3. **Page size auto-detection**: Read page 1 header to determine page size (offset 16-17 in SQLite header), then configure the store accordingly.

4. **Database ID**: Hash of the first page (contains SQLite header + schema) — provides natural deduplication.

### Phase 2: CachedPageSource Integration

**Goal**: Wrap `IndexedDbPageSource` in `CachedPageSource` to minimize JS interop calls.

```csharp
// In SharcDatabase factory
public static async Task<SharcDatabase> OpenFromBrowserAsync(
    IJSRuntime js, string databaseId, SharcOpenOptions? options = null)
{
    var idbSource = await IndexedDbPageSource.OpenAsync(js, databaseId);
    var cached = new CachedPageSource(idbSource, cacheSize: options?.PageCacheSize ?? 500);
    return SharcDatabase.FromPageSource(cached, options);
}
```

**Cache sizing**: Default 500 pages (2 MB) — enough for most schema + hot path pages. Users with larger databases can increase via `SharcOpenOptions.PageCacheSize`.

### Phase 3: Upload & Persist Workflow

**Goal**: Drag-and-drop `.db` files into browser, persisted across sessions.

**User flow**:
1. User drags `.db` file onto upload zone (or uses file picker)
2. JS reads file as `ArrayBuffer`
3. `IndexedDbPageSource.ImportAsync()` splits into pages and stores in IndexedDB
4. `SharcDatabase.OpenFromBrowserAsync()` opens the persisted database
5. Page refresh → database still available, opens instantly from cache

**Blazor component** (`BrowserDatabasePicker.razor`):
```razor
<InputFile OnChange="HandleFileUpload" accept=".db,.sqlite,.sqlite3" />

@code {
    [Parameter] public EventCallback<SharcDatabase> OnDatabaseOpened { get; set; }

    private async Task HandleFileUpload(InputFileChangeEventArgs e)
    {
        var file = e.File;
        var buffer = new byte[file.Size];
        await file.OpenReadStream(maxAllowedSize: 100_000_000).ReadAsync(buffer);

        var dbId = ComputeDatabaseId(buffer);
        await IndexedDbPageSource.ImportAsync(JS, dbId, buffer);

        var db = await SharcDatabase.OpenFromBrowserAsync(JS, dbId);
        await OnDatabaseOpened.InvokeAsync(db);
    }
}
```

### Phase 4: Write-Back Support

**Goal**: Enable writes that persist to IndexedDB (not just reads).

**New type**: `IndexedDbWritablePageSource : IWritablePageSource`

```csharp
public sealed class IndexedDbWritablePageSource : IWritablePageSource, IAsyncDisposable
{
    public long DataVersion { get; }
    public void WritePage(uint pageNumber, ReadOnlySpan<byte> source);
    public void Flush();  // Batch-write dirty pages to IndexedDB
}
```

**Write strategy**:
- `WritePage()` updates the in-memory cache immediately (synchronous)
- Dirty pages tracked in a `HashSet<uint>`
- `Flush()` batch-writes all dirty pages to IndexedDB in a single transaction
- Auto-flush on `Dispose()` or after N writes

### Phase 5: Quota Management & Cleanup

**Goal**: Respect browser storage limits, provide cleanup UI.

```javascript
// Quota check before import
export async function getStorageQuota() {
    const estimate = await navigator.storage.estimate();
    return { usage: estimate.usage, quota: estimate.quota };
}

// List all stored databases with sizes
export async function listDatabases() { ... }

// Delete a stored database
export async function deleteDatabase(databaseId) { ... }

// Request persistent storage (prevents browser eviction)
export async function requestPersistence() {
    return await navigator.storage.persist();
}
```

**C# API**:
```csharp
public static class BrowserStorageManager
{
    public static async Task<StorageQuota> GetQuotaAsync(IJSRuntime js);
    public static async Task<BrowserDatabase[]> ListDatabasesAsync(IJSRuntime js);
    public static async Task DeleteDatabaseAsync(IJSRuntime js, string databaseId);
    public static async Task<bool> RequestPersistenceAsync(IJSRuntime js);
}
```

---

## Architecture Diagram

```
┌─────────────────────────────────────────────────────┐
│  Blazor WASM App                                     │
│                                                      │
│  SharcDatabase.OpenFromBrowserAsync(js, dbId)        │
│       │                                              │
│       ▼                                              │
│  ┌─────────────────┐    ┌──────────────────────┐     │
│  │ CachedPageSource │◄──│ IndexedDbPageSource   │     │
│  │ (LRU, 500 pages) │    │ (JS interop bridge)   │     │
│  └─────────────────┘    └──────┬───────────────┘     │
│                                │ IJSRuntime          │
│  ──────────────────────────────┼─────────────────── │
│                                ▼                     │
│  ┌──────────────────────────────────────────────┐    │
│  │  sharc-page-store.js                         │    │
│  │                                              │    │
│  │  IndexedDB: "sharc-pages"                    │    │
│  │  ├── pages-{dbId}: { pageNum → Uint8Array }  │    │
│  │  └── metadata: { dbId → info }               │    │
│  └──────────────────────────────────────────────┘    │
└─────────────────────────────────────────────────────┘
```

---

## Compatibility Matrix

| Feature | MemoryPageSource (today) | IndexedDbPageSource (new) |
| :--- | :--- | :--- |
| Read operations | Yes | Yes |
| Write operations | Yes (via MemoryPageSource) | Phase 4 |
| Encryption | Yes | Yes (pages stored encrypted) |
| Graph traversal | Yes | Yes |
| Survives refresh | **No** | **Yes** |
| Offline access | No (needs initial fetch) | **Yes** |
| Max database size | Limited by WASM heap (~2 GB) | Limited by IndexedDB quota (~GB+) |
| Cold open speed | Instant (already in RAM) | ~50-200 ms (IndexedDB read + cache warm) |
| Per-page read speed | ~0 ns (span slice) | ~0 ns (from LRU cache after warm) |

---

## Performance Targets

| Metric | Target | Notes |
| :--- | :--- | :--- |
| Import 1 MB database | < 500 ms | Single IndexedDB transaction |
| Import 10 MB database | < 2 s | Chunked transactions |
| Cold open (cached DB) | < 200 ms | Pre-warm header + schema pages |
| Warm page read (cache hit) | < 1 us | Same as MemoryPageSource |
| Cold page read (cache miss) | < 5 ms | Single IndexedDB read |
| Point lookup (warm) | < 500 ns | Slightly above MemoryPageSource due to cache overhead |

---

## Testing Strategy

1. **Unit tests**: Mock `IJSRuntime` to test page source logic without a browser
2. **Integration tests**: Playwright-based tests that run in real browser
3. **Benchmark**: Arena page with "Persistent Mode" toggle comparing memory vs IndexedDB performance
4. **Quota tests**: Verify graceful degradation when storage quota is exhausted

---

## Migration Path

**For existing users** (zero breaking changes):

```csharp
// Before (still works)
byte[] dbBytes = await Http.GetByteArrayAsync("data/myapp.db");
using var db = SharcDatabase.OpenMemory(dbBytes);

// After (opt-in persistence)
// First visit: import from network
byte[] dbBytes = await Http.GetByteArrayAsync("data/myapp.db");
await IndexedDbPageSource.ImportAsync(JS, "myapp", dbBytes);

// Subsequent visits: open from cache (no network!)
using var db = await SharcDatabase.OpenFromBrowserAsync(JS, "myapp");
```

---

## Phase 6: File-Backed IndexedDB Benchmarking

**Goal**: Provide a best-practice pattern for using Sharc as a file-backed cache that streams data into and out of IndexedDB, enabling fairer Arena benchmarks.

**Problem**: Today the Arena benchmarks IndexedDB by serializing entire datasets as JSON through `IJSRuntime` interop. This creates a size-dependent initialization bottleneck (capped at 10K rows) that conflates interop overhead with IndexedDB's actual read/write performance. IndexedDB was winning 5+ of 17 benchmark matchups before — the interop penalty shouldn't disqualify it.

**Approach**: Use the `IndexedDbPageSource` (Phase 1) to stream SQLite pages into IndexedDB as binary blobs, bypassing the JSON serialization bottleneck entirely. The Arena can then benchmark IndexedDB at any scale (100K+ rows) since the data lives as compact binary pages, not inflated JSON objects.

```text
Current (JSON interop, capped at 10K):
  byte[] → SQLite reader → Dictionary<string, object?>[] → JSON → IJSRuntime → IndexedDB

Proposed (binary page streaming, unlimited):
  byte[] → 4KB page chunks → Uint8Array → IndexedDB (page store)
  IndexedDB → cursor over pages → Sharc reads natively
```

**Arena integration**:

- Add a "Persistent Mode" toggle that uses `IndexedDbPageSource` for IndexedDB benchmarks
- IndexedDB benchmarks in persistent mode measure actual IndexedDB read performance (not interop serialization)
- Standard mode (JSON interop) remains available for comparing the browser-native key-value API

This lets IndexedDB compete fairly at all density tiers, including Stress tests.

---

## Open Questions

1. **Should encrypted pages be stored encrypted in IndexedDB?** Recommendation: Yes — if the user opened with encryption options, store pages in their encrypted form. This provides defense-in-depth against browser extension attacks.

2. **Multi-tab coordination**: Should we use `BroadcastChannel` API for cross-tab invalidation? Recommendation: Defer to Phase 6 — single-tab is the 90% case.

3. **Service Worker pre-caching**: Should we add a Service Worker that pre-caches the WASM runtime + common database pages? Recommendation: Defer — this is an Arena enhancement, not a library feature.

4. **Streaming import**: For very large databases (100 MB+), should we stream pages into IndexedDB as they download? Recommendation: Yes, add in Phase 3 as an optimization.

---

## Dependencies

- No new NuGet packages required
- JS module loaded via `IJSRuntime.InvokeAsync` (standard Blazor interop)
- IndexedDB supported in all modern browsers (Chrome 24+, Firefox 16+, Safari 10+, Edge 12+)
- `navigator.storage.estimate()` for quota (Chrome 55+, Firefox 57+, Safari 17+)

---

## Deliverables

| Phase | Deliverable | Effort |
| :--- | :--- | :--- |
| 1 | `IndexedDbPageSource` + JS module | 3-4 days |
| 2 | `CachedPageSource` integration + `OpenFromBrowserAsync` | 1 day |
| 3 | Upload/persist workflow + Blazor component | 2 days |
| 4 | Write-back support (`IWritablePageSource`) | 2-3 days |
| 5 | Quota management + cleanup UI | 1 day |
| **Total** | | **9-11 days** |
