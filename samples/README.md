# Samples

This folder contains runnable examples that demonstrate core Sharc capabilities.
It includes typed 128-bit samples for `GUID`/`UUID` and `FIX128` decimal (28-29 significant digits).

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
| `GuidFix128` | Strict typed 128-bit columns (`GUID`/`UUID` + `FIX128`/`DECIMAL128`) |
| `PointLookup` | Primary-key seek / point lookup path |
| `PrimeExample` | Streaming TopK with custom scoring (spatial nearest-neighbor) |
| `TrustComplex` | Multi-agent trust, authority ceilings, and signatures |
| `UpsertDeleteWhere` | Upsert and predicate-based deletes |
| `VectorSearch` | Embedding storage and nearest-neighbor vector search |
