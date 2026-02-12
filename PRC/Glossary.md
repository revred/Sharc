# Glossary — Sharc

Domain terms used across Sharc documentation and code.

---

### AAD (Associated Authenticated Data)
Additional data passed to an AEAD cipher that is authenticated but not encrypted. In Sharc, the page number is used as AAD to prevent page-swapping attacks.

### AEAD (Authenticated Encryption with Associated Data)
Encryption scheme that provides both confidentiality and integrity. AES-256-GCM and XChaCha20-Poly1305 are AEAD ciphers. The authentication tag detects any tampering with ciphertext or AAD.

### Argon2id
Memory-hard password hashing / key derivation function. Winner of the 2015 Password Hashing Competition. Hybrid of Argon2i (side-channel resistant) and Argon2d (GPU resistant). Sharc's default KDF.

### B-Tree
Balanced tree data structure used by SQLite to store table rows and index entries. SQLite uses two variants: table b-trees (keyed by rowid) and index b-trees (keyed by indexed column values).

### Cell
A single entry in a b-tree page. For table leaf pages, a cell contains a rowid and a record payload. For interior pages, a cell contains a child page pointer and a key.

### Cell Content Area
Region within a b-tree page where cell data is stored. Grows from the end of the page backward toward the cell pointer array.

### Cell Pointer Array
Array of 2-byte offsets immediately following the b-tree page header. Each entry points to the start of a cell within the page. Offsets are big-endian.

### Change Counter
32-bit integer at file header offset 24. Incremented each time the database is modified. Used for cache invalidation and snapshot consistency.

### Column Projection
Optimization technique where only requested columns are decoded from a record. Unrequested columns are skipped by advancing the offset past their bytes without interpreting the data.

### Freelist
Linked list of unused pages in the database file. Managed by SQLite for space reuse. Sharc does not read or modify the freelist.

### GCM (Galois/Counter Mode)
Block cipher mode of operation providing AEAD. Used with AES-256 in Sharc. Produces a 16-byte authentication tag.

### Interior Page
A b-tree page that contains child page pointers and separator keys. Not a leaf. Has page type 0x05 (table) or 0x02 (index).

### KDF (Key Derivation Function)
Function that derives a cryptographic key from a password and salt. Deliberately slow to resist brute-force attacks. Sharc uses Argon2id.

### Leaf Page
A b-tree page that contains actual data (records for tables, key-value pairs for indexes). Has page type 0x0D (table) or 0x0A (index).

### LRU Cache (Least Recently Used)
Caching strategy that evicts the least recently accessed entry when the cache is full. Used by `CachedPageSource` to keep frequently accessed pages in memory.

### Magic String
The 16-byte sequence `"SQLite format 3\0"` at the start of every SQLite database file. Used to identify the file format. Sharc's encrypted format uses `"SHARC\x00"` as its own magic.

### Nonce
Number used once. A value that must never repeat for a given key in AEAD encryption. Sharc derives nonces deterministically from the key and page number.

### Overflow Page
When a record payload is too large to fit in a single b-tree page, the excess is stored in a chain of overflow pages. Each overflow page has a 4-byte pointer to the next page (0 = end of chain).

### Page
Fixed-size block of data. SQLite databases are divided into pages (typically 4096 bytes). All I/O operates in whole pages. Page numbering is 1-based.

### Page Source
Sharc's abstraction (`IPageSource`) for reading pages from any backing store — file, memory, cached.

### Page Transform
Sharc's abstraction (`IPageTransform`) for processing pages during read/write. Primary use: encryption/decryption.

### Pager
In SQLite's C source, the component that manages page I/O, caching, and locking. Sharc's equivalent is the `IPageSource` + `CachedPageSource` combination.

### Payload
The data portion of a b-tree cell. For table leaf cells, this is the serialized record (header + column values).

### Record
A serialized row in SQLite format. Consists of a header (varint-encoded serial types for each column) followed by a body (column values encoded according to their serial types).

### Reserved Bytes
Bytes at the end of each page reserved for extensions (e.g., encryption). Default is 0. Reduces the usable page size: `Usable = PageSize - ReservedBytes`.

### Root Page
The top-level page of a b-tree. For tables and indexes, the root page number is stored in the `sqlite_schema` table. Page 1 is always the root of the `sqlite_schema` table itself.

### RowID
A 64-bit signed integer that uniquely identifies each row in a table (unless the table is WITHOUT ROWID). Equivalent to the implicit `rowid`, `_rowid_`, or `oid` column.

### Schema Cookie
32-bit integer at header offset 40. Incremented when the schema changes. Used by prepared statements to detect stale schema.

### Serial Type
An integer in a record header that describes the storage format and size of a column value. See `FileFormatQuickRef.md` for the full table.

### Sharc Encryption Header
128-byte header prepended to Sharc-encrypted database files. Contains KDF parameters, salt, key verification hash, and cipher metadata.

### sqlite_schema
The system table that stores the database schema. Always rooted at page 1. Contains one row per table, index, view, and trigger. Also known as `sqlite_master` (legacy name).

### Storage Class
SQLite's type system: NULL, INTEGER, REAL (float), TEXT, BLOB. Values in the same column can have different storage classes (SQLite is dynamically typed).

### Usable Page Size
`PageSize - ReservedBytesPerPage`. The number of bytes in a page available for b-tree content. Typically equals `PageSize` (reserved is usually 0).

### Varint (Variable-Length Integer)
SQLite's encoding for integers in 1–9 bytes. Bytes 1–8 use the MSB as a continuation flag with 7 data bits each. Byte 9 (if reached) uses all 8 bits for data. Maximum capacity: 64-bit signed integer.

### VDBE (Virtual Database Engine)
SQLite's bytecode interpreter that executes SQL statements. Sharc does NOT implement a VDBE — it reads the file format directly.

### WAL (Write-Ahead Log)
An alternative journaling mode where changes are written to a separate WAL file before being merged into the main database. Allows concurrent reads during writes. Sharc WAL support is planned for Milestone 8.

### WITHOUT ROWID Table
A table that uses a primary key as the b-tree key instead of an implicit rowid. Uses an index b-tree structure. Sharc support is deferred to post-MVP.
