# SQLite File Format Quick Reference — Sharc

A condensed reference card for the SQLite format 3 structures Sharc must parse.
Full specification: https://www.sqlite.org/fileformat2.html

---

## File Header (100 bytes at offset 0)

```
Offset  Size  Field                          Sharc Usage
──────  ────  ─────                          ───────────
  0      16   Magic: "SQLite format 3\0"     Validate on open
 16       2   Page size (BE)                 CRITICAL — drives all offsets
                Value 1 means 65536
 18       1   Write version (1=legacy,2=WAL) Detect WAL mode
 19       1   Read version (1=legacy,2=WAL)  Detect WAL mode
 20       1   Reserved bytes per page        Usable = PageSize - Reserved
 21       1   Max embedded payload frac (64) Validate = 64
 22       1   Min embedded payload frac (32) Validate = 32
 23       1   Leaf payload fraction (32)     Validate = 32
 24       4   File change counter (BE)       Snapshot consistency
 28       4   Database size in pages (BE)    CRITICAL — page count
 32       4   First freelist trunk page      (unused in read)
 36       4   Total freelist pages           (unused in read)
 40       4   Schema cookie (BE)             Schema change detection
 44       4   Schema format number (BE)      Must be 1–4, Sharc needs ≥4
 48       4   Default page cache size        (informational)
 52       4   Largest root b-tree page       Auto-vacuum (unused)
 56       4   Text encoding (BE)             1=UTF-8, 2=UTF-16LE, 3=UTF-16BE
 60       4   User version (BE)              Expose via SharcDatabaseInfo
 64       4   Incremental vacuum mode        (unused)
 68       4   Application ID (BE)            Expose via SharcDatabaseInfo
 72      20   Reserved (must be zero)        Validate = 0
 92       4   Version-valid-for number       (informational)
 96       4   SQLite version number (BE)     Expose via SharcDatabaseInfo
```

**Note**: Page 1's b-tree header starts at offset 100, not offset 0.

---

## B-Tree Page Header

```
Offset  Size  Field                     Leaf  Interior
──────  ────  ─────                     ────  ────────
  0       1   Page type flag             ✓      ✓
  1       2   First freeblock offset     ✓      ✓
  3       2   Cell count (BE)            ✓      ✓
  5       2   Cell content area start    ✓      ✓
  7       1   Fragmented free bytes      ✓      ✓
  8       4   Right-most child pointer   —      ✓

Header size: 8 bytes (leaf), 12 bytes (interior)
```

### Page Type Flags

```
0x02 = Interior index b-tree
0x05 = Interior table b-tree
0x0A = Leaf index b-tree
0x0D = Leaf table b-tree
```

### Cell Pointer Array

Immediately follows the header. `CellCount` entries, 2 bytes each (BE uint16).
Each value is an offset within the page to the start of a cell.

---

## Table Leaf Cell Structure (page type 0x0D)

```
┌──────────────────┬───────────┬──────────────────────────────┐
│ Payload size      │ RowID     │ Payload (record data)        │
│ (varint)          │ (varint)  │ (inline, possibly overflow)  │
└──────────────────┴───────────┴──────────────────────────────┘
```

## Table Interior Cell Structure (page type 0x05)

```
┌──────────────────┬───────────┐
│ Left child page   │ RowID key │
│ (4 bytes BE)      │ (varint)  │
└──────────────────┴───────────┘
```

---

## Overflow Pages

When inline payload exceeds the usable space threshold:

**Threshold calculation** (table leaf cells):
```
U = usable page size (PageSize - ReservedBytes)
P = payload size
X = U - 35  (maximum inline payload)

If P <= X:       all inline
If P > X:        inline = min(X, M) where M = ((U-12)*32/255) - 23
                 rest goes to overflow pages
```

**Overflow page structure**:
```
┌──────────────────┬──────────────────────────┐
│ Next page number  │ Overflow data             │
│ (4 bytes BE)      │ (up to UsableSize-4 bytes)│
│ 0 = last page     │                           │
└──────────────────┴──────────────────────────┘
```

---

## Varint Encoding

```
Bytes 1–8: [1xxxxxxx] continuation bit + 7 data bits
Byte 9:    [xxxxxxxx] all 8 bits are data (no continuation)

Single byte:  value 0–127
Two bytes:    value 128–16383
...
Nine bytes:   full 64-bit range
```

**Decoding algorithm**:
```
result = 0
for i in 0..7:
    byte = next_byte()
    result = (result << 7) | (byte & 0x7F)
    if byte < 0x80: return result   // No continuation
if more bytes:
    byte = next_byte()              // 9th byte
    result = (result << 8) | byte   // All 8 bits
return result
```

---

## Record Format

```
┌─────────────────────────────┬──────────────────────────────┐
│ HEADER                       │ BODY                          │
│ ┌──────────────────────────┐ │ ┌──────────────────────────┐ │
│ │ Header size (varint)     │ │ │ Column 1 value bytes     │ │
│ │ Column 1 serial type     │ │ │ Column 2 value bytes     │ │
│ │ Column 2 serial type     │ │ │ ...                      │ │
│ │ ...                      │ │ │ Column N value bytes     │ │
│ └──────────────────────────┘ │ └──────────────────────────┘ │
└─────────────────────────────┴──────────────────────────────┘
```

Header size varint includes itself in the byte count.

---

## Serial Type Reference

```
Type   Storage Class   Content Size   Value
────   ─────────────   ────────────   ─────
  0    NULL            0 bytes        NULL
  1    INTEGER         1 byte         Signed int (big-endian)
  2    INTEGER         2 bytes        Signed int (big-endian)
  3    INTEGER         3 bytes        Signed int (big-endian)
  4    INTEGER         4 bytes        Signed int (big-endian)
  5    INTEGER         6 bytes        Signed int (big-endian)
  6    INTEGER         8 bytes        Signed int (big-endian)
  7    FLOAT           8 bytes        IEEE 754 double (big-endian)
  8    INTEGER         0 bytes        Constant value 0
  9    INTEGER         0 bytes        Constant value 1
 10    (reserved)      —              ERROR in Sharc
 11    (reserved)      —              ERROR in Sharc
≥12    BLOB (even)     (N-12)/2       Raw bytes
≥13    TEXT (odd)      (N-13)/2       UTF-8 string bytes
```

**Quick formulas**:
- BLOB size: `(serialType - 12) / 2`
- TEXT size: `(serialType - 13) / 2`
- Is BLOB: `serialType >= 12 && serialType % 2 == 0`
- Is TEXT: `serialType >= 13 && serialType % 2 == 1`

---

## sqlite_schema Table

Page 1 root b-tree. Schema: `(type TEXT, name TEXT, tbl_name TEXT, rootpage INTEGER, sql TEXT)`

```
type      = "table" | "index" | "view" | "trigger"
name      = object name
tbl_name  = associated table name
rootpage  = root b-tree page number (0 for views/triggers)
sql       = CREATE statement (NULL for auto-indexes)
```

---

## Byte Order

**Everything in the file is big-endian** except:
- UTF-16LE text content (when encoding = 2)
- The file itself on disk (just bytes)

Always use `BinaryPrimitives.ReadUInt16BigEndian()` etc.
