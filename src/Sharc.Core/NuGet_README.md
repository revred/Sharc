# Sharc.Core

**Low-level engine internals for the Sharc database engine.**

B-tree traversal, page I/O, record decoding, varint primitives, and write engine — the foundation layer that powers all Sharc packages. Pure C#, zero native dependencies.

## Features

- **B-Tree Layer**: Generic `BTreeReader<T>` and `BTreeCursor<T>` with JIT-specialized cell parsing.
- **Page I/O**: `IPageSource` abstraction for File, Memory, Mmap, and Cached page access.
- **Record Codec**: Zero-allocation record decoding via `ReadOnlySpan<byte>` directly on page buffers.
- **Varint Primitives**: `VarintDecoder` and `SerialTypeCodec` for SQLite wire format parsing.
- **Write Engine**: `PageManager`, `CellBuilder`, `BTreeMutator` for B-tree splits and ACID transactions.
- **Schema Reader**: Parses `sqlite_schema` table into structured `TableInfo` and `ColumnInfo`.

## Note

This is an internal infrastructure package. Most users should reference the top-level [`Sharc`](https://www.nuget.org/packages/Sharc) package instead, which re-exports the public API surface.

[Full Documentation](https://github.com/revred/Sharc)
