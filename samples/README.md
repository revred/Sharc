# Samples

This folder contains runnable examples that demonstrate core Sharc capabilities.

## Run all samples

Requires the `dotnet-script` tool (`dotnet tool install -g dotnet-script`).

```bash
dotnet script ./samples/run-all.csx -- --build-only
dotnet script ./samples/run-all.csx
```

`--build-only` is useful in CI or when you only want compilation coverage.

## Sample map

| Sample | Focus |
|---|---|
| `ApiComparison` | End-to-end Sharc vs SQLite API comparison with timing |
| `BasicRead` | Minimal table scan with projected columns |
| `BrowserOpfs` | Browser/OPFS integration patterns and portability notes |
| `BulkInsert` | Transactional batch writes with `SharcWriter` |
| `ContextGraph` | Graph node lookup, zero-allocation edge cursor, BFS traversal |
| `EncryptedRead` | Opening encrypted databases with decryption options |
| `FilterAndProject` | Column filters and projection pipelines |
| `PointLookup` | Primary-key seek / point lookup path |
| `TrustComplex` | Multi-agent trust, authority ceilings, and signatures |
| `UpsertDeleteWhere` | Upsert and predicate-based deletes |
| `VectorSearch` | Embedding storage and nearest-neighbor vector search |
