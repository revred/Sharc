# Data Structure Tests — Inference-Based Strategy

## Purpose

This document defines the testing philosophy and specific test plan for Sharc's **core data structures** — the readonly structs, value types, decoders, and parsers that form the foundation of the SQLite format reader. These tests go beyond simple happy-path coverage: they verify **inferred behaviors** derived from the [SQLite file format specification](https://www.sqlite.org/fileformat2.html), boundary conditions, and cross-component invariants.

## Philosophy: Inference-Based Testing

An **inference-based test** doesn't just check "does function X return Y for input Z." It tests a behavior that can be **deduced from the specification** but isn't immediately obvious from reading the code. Examples:

| Category | Example |
|----------|---------|
| **Format invariant** | A record can have fewer columns than the schema defines — missing columns are implicitly NULL (this is how `ALTER TABLE ADD COLUMN` works without rewriting existing records) |
| **Encoding edge case** | Serial types 8 and 9 represent integer constants 0 and 1 with **zero body bytes** — the entire value is encoded in the type code |
| **Computed threshold** | Overflow threshold `X = U - 35` and minimum inline `M = ((U-12)*32/255) - 23` are non-obvious formulas derived from SQLite's page layout constraints |
| **Sign extension** | 3-byte and 6-byte signed integers are not standard CPU widths — manual sign extension from bit 23/47 is required |
| **Special encoding** | Page size value 1 in the header means 65536; cell content offset 0 means 65536 |
| **Behavioral contract** | `ColumnValue.AsInt64()` on a NULL value returns 0 (not throw) — matching SQLite's coercion semantics |
| **Cross-component** | INTEGER PRIMARY KEY columns store NULL in the record body; the rowid from the b-tree key is the real value |

## ADR: Test Assertion Rules

**ADR-013** (see DecisionLog.md): All tests use **plain xUnit `Assert`** methods. No FluentAssertions, no Shouldly, no custom assertion libraries.

**Rationale:**
- Zero additional dependency surface
- Assertion failures produce clear, unambiguous messages
- `Assert.Equal`, `Assert.True`, `Assert.Throws<T>` cover 95% of needs
- `Assert.All` for collection invariants; `Assert.InRange` for bounds

## Test Organization

```
tests/Sharc.Tests/
  DataStructures/
    ColumnValueInferenceTests.cs       — ColumnValue struct behaviors
    DatabaseHeaderInferenceTests.cs    — Header parsing edge cases
    BTreePageHeaderInferenceTests.cs   — Page header + cell pointer contracts
    RecordDecoderInferenceTests.cs     — Record format edge cases
    CellParserInferenceTests.cs        — Cell parsing thresholds
    SchemaModelTests.cs                — Schema model contracts
    CreateTableParserEdgeCaseTests.cs  — SQL parsing edge cases
    BTreeCursorAdvancedTests.cs        — Deep traversal scenarios
```

---

## Test Plans by Data Structure

### 1. ColumnValue (readonly struct)

**Source:** `src/Sharc.Core/IRecordDecoder.cs`

The ColumnValue struct is the fundamental unit of decoded data. It uses inline storage (long, double) for primitives and `ReadOnlyMemory<byte>` for variable-length data. Tests should verify:

| Test | Inference Source |
|------|-----------------|
| `Null_AsInt64_ReturnsZero` | SQLite coercion: NULL → 0 for numeric contexts |
| `Null_AsDouble_ReturnsZero` | Same coercion rule for float |
| `Null_AsString_ReturnsEmpty` | UTF-8 decode of empty span returns "" |
| `Integer_MaxValue_PreservesExactly` | int64 storage must not truncate |
| `Integer_MinValue_PreservesExactly` | Two's complement minimum |
| `Integer_Constants8And9_ZeroBodyBytes` | Serial types 8/9 encode constants 0/1 inline |
| `Float_PositiveInfinity_RoundTrips` | IEEE 754 special values |
| `Float_NaN_RoundTrips` | IEEE 754 NaN preservation |
| `Float_NegativeZero_Preserved` | -0.0 vs +0.0 distinction |
| `Text_EmptyString_NotNull` | Empty string has StorageClass.Text, not Null |
| `Blob_EmptyBlob_NotNull` | Empty blob has StorageClass.Blob, not Null |
| `StorageClass_AllFactories_CorrectClass` | Each factory returns expected enum value |
| `SerialType_Preserved_ForAllFactories` | SerialType round-trips through the struct |

### 2. DatabaseHeader (readonly struct)

**Source:** `src/Sharc.Core/Format/DatabaseHeader.cs`

| Test | Inference Source |
|------|-----------------|
| `Parse_PageSize1_Returns65536` | SQLite spec: page size field value 1 → 65536 bytes |
| `Parse_ReservedBytesPerPage_AffectsUsableSize` | `UsablePageSize = PageSize - ReservedBytesPerPage` |
| `Parse_ReservedBytes64_ReducesUsableSize` | Common encrypted database pattern (64 reserved) |
| `Parse_TextEncodingUtf16Le_ReturnsValue2` | Field offset 56, values 1/2/3 |
| `Parse_TextEncodingUtf16Be_ReturnsValue3` | Same field, big-endian variant |
| `Parse_WalMode_WriteVersion2_Detected` | WAL = WriteVersion == 2 or ReadVersion == 2 |
| `Parse_SchemaFormat4_ReturnsFour` | Schema format at offset 44, range 1–4 |
| `Parse_ApplicationId_PreservesUint32` | Offset 68, 4-byte big-endian |
| `Parse_Exactly100Bytes_Succeeds` | Minimum valid input length |
| `HasValidMagic_First15BytesMatch_FalseIfByte16Wrong` | Null terminator at byte 15 is part of magic |

### 3. BTreePageHeader (readonly struct)

**Source:** `src/Sharc.Core/Format/BTreePageHeader.cs`

| Test | Inference Source |
|------|-----------------|
| `Parse_CellContentOffset0_Means65536` | SQLite spec: 0 in this field = 65536 |
| `Parse_InteriorTable_HeaderSize12` | 12-byte header (8 base + 4 right-child pointer) |
| `Parse_LeafTable_HeaderSize8` | 8-byte header (no right-child pointer) |
| `Parse_InteriorIndex_HeaderSize12` | Same as interior table |
| `Parse_LeafIndex_HeaderSize10` | 10-byte header (different from leaf table!) |
| `ReadCellPointers_PointersArePageAbsolute` | Pointers are offsets from start of page, not from header |
| `ReadCellPointers_OrderMatchesBTreeKeyOrder` | Cell i has key ≤ cell i+1's key |
| `Parse_CellCount0_ValidEmptyPage` | Zero cells is valid (e.g., after all rows deleted) |
| `Parse_InvalidPageType_ThrowsCorruptPage` | Byte values other than 0x02/0x05/0x0A/0x0D |

### 4. RecordDecoder (sealed class)

**Source:** `src/Sharc.Core/Records/RecordDecoder.cs`

| Test | Inference Source |
|------|-----------------|
| `DecodeRecord_EmptyRecord_HeaderOnly_ZeroColumns` | A record can have header_size=1 and no columns |
| `DecodeRecord_SerialType8_IntegerConstant0` | Type 8 → Integer 0, zero body bytes |
| `DecodeRecord_SerialType9_IntegerConstant1` | Type 9 → Integer 1, zero body bytes |
| `DecodeRecord_Int24_Positive_NoSignExtension` | 0x7FFFFF → +8388607 |
| `DecodeRecord_Int24_Negative_SignExtensionFromBit23` | 0x800000 → -8388608 |
| `DecodeRecord_Int48_Negative_SignExtensionFromBit47` | 0x800000000000 → negative |
| `DecodeRecord_FewerColumnsThanExpected_TrailingNulls` | Records from before ALTER TABLE ADD COLUMN have fewer columns |
| `DecodeRecord_ManyColumns_100Columns_AllDecoded` | Header can encode many varint serial types |
| `DecodeRecord_TextSerialType13_EmptyString` | (13-13)/2 = 0 bytes → empty string |
| `DecodeRecord_BlobSerialType12_EmptyBlob` | (12-12)/2 = 0 bytes → empty blob |
| `DecodeRecord_LargeText_SerialType1000_Correct` | (1000-13)/2 = 493 bytes of text (odd serial ≥ 13) |
| `DecodeRecord_LargeBlob_SerialType1000_Fails` | 1000 is even → blob, not text; (1000-12)/2 = 494 bytes |
| `GetColumnCount_EmptyRecord_ReturnsZero` | header_size = 1, no serial type varints |
| `DecodeColumn_IndexBeyondCount_ThrowsArgumentOutOfRange` | Bounds checking on column index |
| `DecodeColumn_Index0_SkipsNothing` | First column decoded directly |
| `DecodeColumn_LastColumn_SkipsPrecedingBody` | Column projection skip logic |

### 5. CellParser (static class)

**Source:** `src/Sharc.Core/BTree/CellParser.cs`

| Test | Inference Source |
|------|-----------------|
| `CalculateInlinePayloadSize_ExactlyAtThreshold_ReturnsFullSize` | payload == X → all inline |
| `CalculateInlinePayloadSize_OneOverThreshold_ReturnsM` | payload == X+1 → only M inline |
| `CalculateInlinePayloadSize_MassivePayload_StillReturnsM` | payload = 1GB → M unchanged |
| `ParseTableLeafCell_ZeroPayload_ReturnsZero` | Valid but empty record |
| `ParseTableLeafCell_RowId0_Valid` | SQLite allows rowid 0 |
| `ParseTableLeafCell_MaxRowId_Valid` | rowid can be up to 2^63-1 |
| `GetOverflowPage_NonZero_IndicatesOverflow` | Standard overflow pointer |
| `GetOverflowPage_AtOffset_ReadsCorrectPosition` | Offset parameter matters for reading at non-zero position |

### 6. SharcSchema (sealed class)

**Source:** `src/Sharc.Core/Schema/SchemaModels.cs`

| Test | Inference Source |
|------|-----------------|
| `GetTable_CaseInsensitive_FindsMatch` | SQLite table names are case-insensitive |
| `GetTable_NotFound_ThrowsKeyNotFound` | Clean error for missing table |
| `GetTable_ExactMatch_ReturnsCorrectTable` | When multiple tables exist |
| `Tables_ImmutableList_CannotBeModified` | IReadOnlyList contract |
| `ColumnInfo_Ordinal_MatchesDeclarationOrder` | Column 0 is first declared column |
| `TableInfo_IsWithoutRowId_DetectedFromSql` | WITHOUT ROWID in CREATE TABLE SQL |
| `IndexInfo_IsUnique_DetectedFromSql` | UNIQUE in CREATE INDEX SQL |

### 7. CreateTableParser (static class)

**Source:** `src/Sharc.Core/Schema/CreateTableParser.cs`

| Test | Inference Source |
|------|-----------------|
| `ParseColumns_NoParens_ReturnsEmpty` | Malformed SQL edge case |
| `ParseColumns_EmptyBody_ReturnsEmpty` | `CREATE TABLE t ()` |
| `ParseColumns_OnlyConstraints_ReturnsEmpty` | `CREATE TABLE t (PRIMARY KEY (a, b))` |
| `ParseColumns_Autoincrement_ParsesColumn` | AUTOINCREMENT is a column constraint, not table |
| `ParseColumns_GeneratedColumn_StopsAtKeyword` | GENERATED ALWAYS AS ... |
| `ParseColumns_NestedParens_InType_Handles` | VARCHAR(255) includes the parens |
| `ParseColumns_MixedQuoteStyles_AllWork` | "col1", [col2], \`col3\` in same table |
| `ParseColumns_ReservedWordColumn_QuotedCorrectly` | `"select"`, `[table]` etc. |

### 8. BTreeCursor (advanced scenarios)

**Source:** `src/Sharc.Core/BTree/BTreeCursor.cs`

| Test | Inference Source |
|------|-----------------|
| `MoveNext_ThreeLevelTree_EnumeratesAllLeaves` | Interior→Interior→Leaf chain |
| `MoveNext_SingleCellPerLeaf_StillTraversesAll` | Degenerate tree where each leaf has 1 row |
| `MoveNext_RowIds_AlwaysAscending` | B-tree invariant: left < key < right |
| `Payload_AccessBeforeMoveNext_ThrowsOrReturnsDefault` | Contract: must call MoveNext first |
| `MoveNext_AfterFullTraversal_ReturnsFalseRepeatedly` | Idempotent end-of-data |

---

## Verification Checklist

After implementing all tests:

```bash
dotnet test tests/Sharc.Tests --filter "FullyQualifiedName~DataStructures" --verbosity normal
dotnet test                    # ALL tests still green
dotnet build                   # 0 warnings, 0 errors
```

## Relationship to Other Test Documents

- **TestStrategy.md** — defines the 4-layer test approach (unit → integration → property → benchmark)
- **DataStructureTests.md** (this document) — deep-dives into core struct/class unit test design
- **DecisionLog.md** — ADR-012/013 for xUnit Assert adoption
