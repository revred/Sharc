# WAL Read Support Design — Sharc (Milestone 8)

## 1. Overview

SQLite's Write-Ahead Log (WAL) mode is the most common journal mode for applications requiring concurrent reads during writes. Sharc must be able to read WAL-mode databases correctly to be useful in real-world scenarios.

**Goal**: Read a consistent snapshot of a WAL-mode database without interfering with writers.

## 2. WAL Architecture

### 2.1 Files Involved

A WAL-mode database consists of three files:

```
mydata.db       — Main database file (may be stale)
mydata.db-wal   — Write-ahead log (recent changes)
mydata.db-shm   — Shared memory index (WAL frame lookup)
```

### 2.2 How WAL Works (Simplified)

1. Writer appends **frames** to the WAL file instead of modifying the main database
2. Each frame contains a page number and the modified page data
3. Readers check the WAL for the most recent version of any page before falling back to the main database
4. Periodically, a **checkpoint** merges WAL frames back into the main database

### 2.3 What Sharc Needs to Do

For **read-only** access:

1. Detect WAL mode from database header (read version = 2)
2. Parse the WAL file header
3. Build a frame index: `page_number → latest WAL frame offset`
4. When reading page N:
   - Check frame index for page N
   - If found: read from WAL file at frame offset
   - If not found: read from main database file
5. Ensure snapshot consistency via salt values and checksums

## 3. WAL File Format

### 3.1 WAL Header (32 bytes)

```
Offset  Size  Field
──────  ────  ─────
  0       4   Magic number (0x377f0682 or 0x377f0683)
  4       4   File format version (3007000)
  8       4   Database page size
 12       4   Checkpoint sequence number
 16       4   Salt-1 (random, changes each WAL reset)
 20       4   Salt-2 (random, changes each WAL reset)
 24       4   Checksum-1 (of first 24 bytes)
 28       4   Checksum-2 (continuation)
```

### 3.2 WAL Frame Header (24 bytes per frame)

```
Offset  Size  Field
──────  ────  ─────
  0       4   Page number (1-based)
  4       4   For commit frames: database size in pages after commit; else 0
  8       4   Salt-1 (must match WAL header)
 12       4   Salt-2 (must match WAL header)
 16       4   Checksum-1 (cumulative of WAL header + all frames up to this one)
 20       4   Checksum-2 (continuation)
```

### 3.3 WAL Frame Data

Immediately after each frame header: `page_size` bytes of page data.

### 3.4 Frame Layout

```
[WAL Header: 32 bytes]
[Frame 1 Header: 24 bytes][Frame 1 Data: page_size bytes]
[Frame 2 Header: 24 bytes][Frame 2 Data: page_size bytes]
...
[Frame N Header: 24 bytes][Frame N Data: page_size bytes]
```

Total frame size: `24 + page_size` bytes.

## 4. WAL Index (shm file)

### 4.1 Purpose

The WAL index (`-shm` file) is a shared-memory region that provides O(1) lookup of WAL frames by page number. It's created by the first connection that opens the database in WAL mode.

### 4.2 Sharc's Approach

**Option A (Preferred)**: Build the frame index ourselves by scanning the WAL file sequentially. This avoids parsing the complex shm format and works even when the shm file doesn't exist.

**Option B**: Parse the shm file for faster startup on large WAL files.

**Decision**: Option A for v0.1 WAL support. The WAL file is typically small (< 10K frames), so sequential scan is fast enough.

### 4.3 Building the Frame Index

```csharp
// Pseudocode
var frameIndex = new Dictionary<uint, long>(); // page_number → file_offset

long offset = 32; // Skip WAL header
while (offset + 24 + pageSize <= walFileLength)
{
    var frameHeader = ReadFrameHeader(walFile, offset);

    // Validate salt matches WAL header
    if (frameHeader.Salt1 != walHeader.Salt1 || frameHeader.Salt2 != walHeader.Salt2)
        break; // WAL was reset; frames beyond here are stale

    // Validate checksum chain
    if (!VerifyChecksum(frameHeader, cumulativeChecksum))
        break; // Corrupt frame; stop here

    // Record latest frame for this page (overwrites earlier frames)
    frameIndex[frameHeader.PageNumber] = offset + 24; // Data starts after frame header

    offset += 24 + pageSize;
}
```

## 5. WalPageSource Implementation

```csharp
internal sealed class WalPageSource : IPageSource
{
    private readonly IPageSource _mainDbSource;    // Main database file
    private readonly FileStream _walFile;           // WAL file
    private readonly Dictionary<uint, long> _frameIndex;  // Page → WAL offset
    private readonly int _pageSize;
    private readonly int _pageCount;  // From latest commit frame

    public int PageSize => _pageSize;
    public int PageCount => _pageCount;

    public ReadOnlySpan<byte> GetPage(uint pageNumber)
    {
        if (_frameIndex.TryGetValue(pageNumber, out long walOffset))
        {
            // Read from WAL file
            return ReadFromWal(walOffset);
        }
        else
        {
            // Fall back to main database
            return _mainDbSource.GetPage(pageNumber);
        }
    }
}
```

## 6. Snapshot Consistency

### 6.1 Problem

A WAL-mode database can be modified while Sharc is reading. Without coordination, Sharc might read an inconsistent mix of old and new pages.

### 6.2 Solution: Salt-Based Snapshot

1. On open, read the WAL header and record `salt1` and `salt2`
2. Build the frame index, stopping when salts change (indicates WAL reset)
3. Find the **last commit frame** (one with `database_size > 0`)
4. Use only frames up to and including the last commit frame
5. This gives a consistent snapshot at the time of that commit

### 6.3 No Locking Required (Read-Only)

SQLite's WAL locking protocol (`-shm` file) is designed for coordinating readers and writers. Since Sharc is read-only and builds its own frame index:

- Sharc does NOT modify the `-shm` file
- Sharc does NOT acquire any locks
- Sharc relies on atomic reads of the WAL header for consistency
- If the WAL is being actively written during open, Sharc gets a snapshot up to the last complete commit frame — this is safe

### 6.4 Limitations

- Sharc's snapshot may be slightly behind the latest commit (acceptable for read-only use)
- If a checkpoint completes between reading the main DB header and the WAL, Sharc might miss frames that were folded in. Mitigation: re-read main DB header and rebuild frame index if change counter differs.

## 7. Checksum Verification

### 7.1 SQLite WAL Checksum Algorithm

SQLite uses a custom checksum that processes data in 8-byte chunks:

```
function wal_checksum(data, s0, s1):
    for i in range(0, len(data), 8):
        s0 += read_uint32(data, i) + s1
        s1 += read_uint32(data, i+4) + s0
    return (s0, s1)
```

The byte order of the uint32 reads depends on the WAL magic number:
- `0x377f0682`: big-endian
- `0x377f0683`: little-endian (matches native byte order on most systems)

### 7.2 Verification Chain

1. Verify WAL header checksum (bytes 0–23 → checksum at bytes 24–31)
2. For each frame: cumulative checksum includes frame header bytes 0–7 + page data
3. Frame is valid only if cumulative checksum matches frame header checksum
4. Stop processing at first checksum mismatch (partial write / corruption)

## 8. Integration Architecture

```
SharcDatabase.Open("mydata.db")
    │
    ├── Detect WAL mode from header
    │
    ├── If WAL:
    │     ├── Open "mydata.db-wal"
    │     ├── Build frame index
    │     ├── Determine snapshot page count
    │     └── Create WalPageSource(mainSource, walFile, frameIndex)
    │
    └── If not WAL:
          └── Create FilePageSource("mydata.db")
    │
    ▼
CachedPageSource(walPageSourceOrFilePageSource)
```

## 9. Test Plan

| Test | Description |
|------|-------------|
| WAL detection | Open WAL-mode DB, verify `IsWalMode = true` |
| Frame index building | WAL with known frames, verify index contents |
| Page override | Page in WAL overrides main DB page |
| Page fallback | Page NOT in WAL reads from main DB |
| Multiple commits | Latest commit frame wins |
| WAL reset | Salt change terminates frame scanning |
| Checksum validation | Corrupt frame detected and skipped |
| Commit snapshot | Only committed frames are visible |
| Empty WAL | DB with WAL mode but no uncommitted changes |
| Large WAL | 10K+ frames, performance validation |

## 10. Deferred Concerns

- **Checkpoint awareness**: Sharc does not checkpoint. If the WAL grows very large, performance degrades. This is the writer's responsibility.
- **WAL2**: SQLite's experimental WAL2 format is not supported.
- **Concurrent snapshot refresh**: Once opened, Sharc's snapshot is fixed. Refreshing requires reopening.
