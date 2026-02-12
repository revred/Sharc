# Compatibility Matrix — Sharc

## SQLite Feature Support Status

### Legend

| Symbol | Meaning |
|--------|---------|
| ✅ | Supported in current version |
| 🔶 | Planned for a specific milestone |
| ❌ | Not planned / out of scope |
| ⚠️ | Partial support with caveats |

---

## File Format

| Feature | Status | Milestone | Notes |
|---------|--------|-----------|-------|
| Format 3 magic validation | ✅ | M1 | |
| Page sizes 512–65536 | ✅ | M1 | Including value-1-means-65536 |
| Page size = power of 2 validation | ✅ | M1 | |
| Reserved bytes per page | ✅ | M1 | Usable = PageSize - Reserved |
| Schema format 4 | ✅ | M1 | Default for modern SQLite |
| Schema format 1–3 | ⚠️ | M1 | Parsed but may lack features |
| Big-endian header fields | ✅ | M1 | Via BinaryPrimitives |
| File change counter | ✅ | M2 | Read, not written |
| Schema cookie | ✅ | M5 | Read, not written |

## Text Encoding

| Feature | Status | Milestone | Notes |
|---------|--------|-----------|-------|
| UTF-8 (encoding = 1) | ✅ | M1 | Primary target |
| UTF-16LE (encoding = 2) | 🔶 | Post-MVP | Architecture supports it |
| UTF-16BE (encoding = 3) | 🔶 | Post-MVP | Architecture supports it |

## Page Types

| Feature | Status | Milestone | Notes |
|---------|--------|-----------|-------|
| Table leaf (0x0D) | ✅ | M3 | Core functionality |
| Table interior (0x05) | ✅ | M3 | Core functionality |
| Index leaf (0x0A) | 🔶 | M7 | Needed for index reads |
| Index interior (0x02) | 🔶 | M7 | Needed for index reads |
| Freelist trunk pages | ❌ | — | Write/compact only |
| Freelist leaf pages | ❌ | — | Write/compact only |
| Overflow pages | ✅ | M3 | Following overflow chains |
| Pointer map pages | ❌ | — | Auto-vacuum only |
| Lock byte page | ❌ | — | Not needed for reads |
| Lock byte page | ❌ | — | Not needed for reads |

## B-Tree Operations

| Feature | Status | Milestone | Notes |
|---------|--------|-----------|-------|
| Table b-tree sequential scan | ✅ | M3 | Full table scan |
| Table b-tree rowid lookup | ✅ | M7 | Binary search via `BTreeCursor.Seek` |
| Index b-tree sequential scan | 🔶 | M7 | |
| Index b-tree key lookup | 🔶 | M7 | |
| Overflow page following | ✅ | M3 | Linked list traversal |
| Cell pointer array reading | ✅ | M3 | |

## Platform Support

| Platform | Status | Notes |
|----------|--------|-------|
| .NET 10 (Windows/Linux/macOS) | ✅ | Current development target |
| **Blazor WebAssembly** | ✅ | **TIER 1 SUPPORT**. Optimized binary size via Trimming. |
| **Native AOT** | ✅ | **FULLY COMPATIBLE**. No reflection or dynamic code generation. |
| Docker (CGroup tracking) | ✅ | Optimized for resource-constrained environments. |
| .NET Framework 4.x | ❌ | Out of scope. |

## Graph Features (Sharc.Graph)

| Feature | Status | Notes |
|---------|--------|-------|
| Concept Lookup (O(log N)) | ✅ | Via `ConceptStore` |
| Relation Retrieval (O(M)) | ✅ | Initial implementation (Table Scan) |
| Relational Traversal (O(log M)) | 🔶 | Pending Index Reader integration |
| Schema Adaptation | ✅ | Dynamic SQLite to Graph ontology mapping |
| Token Budgeting | 🔶 | Context expansion for AI prompts |
