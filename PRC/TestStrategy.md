# Test Strategy — Sharc

## 1. Testing Layers

### Layer 1: Unit Tests (`Sharc.Tests`)
Pure function tests on isolated components. No disk I/O.

| Component | Test Focus |
|-----------|-----------|
| `VarintDecoder` | Known value encoding/decoding, boundary values (0, 1, max), malformed input |
| `SerialTypeCodec` | All 12+ serial types, size calculation, NULL/integer/float/blob/text |
| `PageHeader` | Parse all 4 b-tree page types, page 1 offset adjustment |
| `BTreeReader` | Leaf cell enumeration, interior page traversal, overflow following |
| `RecordDecoder` | Multi-column records, all column types, empty records |
| `DatabaseHeader` | Magic validation, page size extraction, encoding detection |
| `SchemaParser` | sqlite_schema row interpretation, type/name/rootpage extraction |
| `PageCache` | LRU eviction, hit/miss counting, capacity enforcement |
| `PageTransform` | Encrypt/decrypt round-trip, AAD verification, tamper detection |
| `KeyDerivation` | Known-answer tests for Argon2id, key verification |
| `AgentRegistry` | ECDSA self-attestation, agent registration, cache loading |
| `LedgerManager` | Hash-chain integrity, append/verify, signature validation |
| `ReputationEngine` | Agent scoring, decay, threshold enforcement |
| `CoSignature` | Multi-agent endorsement, attestation chaining |
| `Governance` | Permission policies, scope boundaries, read/write access |
| `RecordEncoder` | Value serialization to SQLite record format |
| `CellBuilder` | B-tree leaf cell construction from encoded records |
| `BTreeMutator` | Leaf insert, page split, overflow handling |

### Layer 2: Integration Tests (`Sharc.IntegrationTests`)
End-to-end tests against real SQLite database files.

| Scenario | Description |
|----------|-------------|
| `EmptyDatabase` | Open DB with no user tables, verify schema is empty |
| `SingleTableScan` | Create DB with known data, read all rows, verify values |
| `MultipleTypes` | INTEGER, REAL, TEXT, BLOB, NULL across columns |
| `LargeRecords` | Records spanning overflow pages |
| `MultiTable` | Multiple tables, correct schema enumeration |
| `LargeDatabase` | 100K+ rows, performance sanity check |
| `MemorySource` | Read from `ReadOnlyMemory<byte>` buffer |
| `ConcurrentRead` | Multiple readers on same file |
| `Utf8Encoding` | Verify correct text handling |
| `EncryptedDatabase` | Open and read Sharc-encrypted DB |

### Layer 3: Property/Fuzz Tests (v0.2)
- Random varint values: encode → decode = identity
- Random records: serialize → deserialize = identity
- Malformed page headers: no crashes, clean exceptions
- Random byte sequences as database files: no crashes

### Layer 4: Benchmarks (`Sharc.Benchmarks`)
- Varint decode throughput
- Page read throughput (file vs memory)
- Full table scan (compare with `Microsoft.Data.Sqlite`)
- Record decode throughput
- Allocation profiling (should trend toward zero)

## 2. Test Data Strategy

### Approach: Generate test databases at build time

A `tools/GenerateTestDatabases.csx` script (or pre-generated fixtures) creates
SQLite databases with known content using `Microsoft.Data.Sqlite`:

- `empty.db` — no user tables
- `simple.db` — one table, 10 rows, all column types
- `overflow.db` — records with large BLOBs triggering overflow
- `multi_table.db` — 5 tables with varied schemas
- `large.db` — 100K rows for performance testing
- `utf8.db` — Unicode text in various scripts

Test databases are checked into `tests/Sharc.Tests/TestData/` (small files)
or generated on CI (large files).

### In-memory test data

Many unit tests construct byte arrays directly — no file I/O needed.
Example: `VarintDecoderTests` use hardcoded byte sequences.

## 3. TDD Workflow

```
1. Write a failing test that defines expected behavior
2. Run test → RED (compilation error or assertion failure)
3. Write minimum implementation to pass
4. Run test → GREEN
5. Refactor for clarity and performance
6. Run all tests → still GREEN
7. Commit
```

## 4. Test Naming Convention

```
[MethodUnderTest]_[Scenario]_[ExpectedResult]

Examples:
- DecodeVarint_SingleByteZero_ReturnsZero
- ParsePageHeader_LeafTablePage_ReturnsCorrectCellCount
- ReadTable_EmptyTable_ReturnsNoRows
- DecryptPage_TamperedCiphertext_ThrowsAuthenticationException
```

## 5. CI Requirements

- All unit tests must pass on every commit (currently 1,064 tests across 5 projects)
- Integration tests run on PR merge
- Benchmarks run nightly, results tracked
- Code coverage target: 90%+ for Sharc.Core
- Trust tests must validate ECDSA signatures with real EC keys (no random byte stubs)
