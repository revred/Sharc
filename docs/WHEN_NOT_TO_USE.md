# When Not To Use Sharc

Sharc is a high-performance specialized engine. It is not a drop-in replacement for a full RDBMS.

## Use Standard SQLite instead of Sharc if:

1. **You need complex SQL**: Sharc does not have a SQL parser. You cannot run `SELECT a, SUM(b) FROM table GROUP BY a`.
2. **You need JOINs**: Sharc reads tables individually. Cross-table joining must be done manually in your application logic or via the Graph Layer.
3. **You have many writes/updates**: Sharc's write engine is in its early stages (Phase 1). It is optimized for "append-only" or "bulk-create" workloads. For complex transactions and transactional stability on writes, use standard SQLite.
4. **You need FTS5/R-Tree**: Specialized SQLite virtual tables are not currently supported.
5. **You need Legacy Support**: Sharc targets modern .NET. For legacy environments or non-.NET languages, use the official C library.

## Summary

| Feature | Sharc | Standard SQLite |
|:---|:---:|:---:|
| Read Performance | **Best** | Good |
| SQL Support | None | **Full** |
| Write Support | Basic (Phase 1) | **Complete** |
| Deployment | Zero-dependency | Requires native DLL |
| AI Context Prep | **Optimized** | Generic |
