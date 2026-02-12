# Data Structure Tests — Inference-Based Strategy

## Purpose
This document defines the testing philosophy and specific test plan for Sharc's core data structures, including recent Phase 2 additions like Graph Storage and B-Tree point lookups.

## Philosophy: Inference-Based Testing
We test behaviors deduced from the SQLite specification that are not immediately obvious.

| Category | Example |
|----------|---------|
| **Seek Navigation** | `BTreeCursor.Seek(rowId)` must perform a binary search on interior pages (O(log N)) rather than a scan. |
| **Schema Adaptation** | `GenericSchemaAdapter` must handle non-standard column names by mapping them to core graph concepts (ConceptKind, RelationKind). |

---

## Test Plans by Data Structure

### 8. BTreeCursor (Seek & Navigation)
**Source:** `src/Sharc.Core/BTree/BTreeCursor.cs`

| Test | Inference Source |
|------|-----------------|
| `Seek_RowIdInLeaf_PositionsCorrectly` | Direct hit on current page. |
| `Seek_RowIdInNextLeaf_TraversesRight` | Parent traversal to next siblings. |
| `Seek_RowIdInPreviousLeaf_TraversesLeft` | Parent traversal to previous siblings. |
| `Seek_NonExistentRowId_PositionsAtNextGreater` | Near-neighbor positioning. |

### 9. Schema Adapters (Graph)
**Source:** `src/Sharc.Graph/Schema/GenericSchemaAdapter.cs`

| Test | Inference Source |
|------|-----------------|
| `MapConcept_CustomColumnNames_ResolvesCorrectly` | Adapter configuration for alias/kind columns. |
| `MapRelation_DifferentTableStructure_Adapts` | Handling databases where relations are not in `_relations`. |

## Verification Checklist
```bash
dotnet test tests/Sharc.Tests --filter "FullyQualifiedName~DataStructures"
dotnet test tests/Sharc.Graph.Tests.Unit
```
