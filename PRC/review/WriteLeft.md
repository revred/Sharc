# WriteLeft Findings And Recommendations

Date: 2026-02-18
Branch: `WriteLeft` (live/in-progress)

## Verification Snapshot
1. `dotnet build src/Sharc/Sharc.csproj -c Release --nologo` passes.
2. `dotnet test tests/Sharc.Tests/Sharc.Tests.csproj -c Release --nologo --filter "FullyQualifiedName~JoinTests|FullyQualifiedName~ViewTests"` passes (10/10).

## Findings & Remediations (Status: FIXED)

All findings identified in the WriteLeft audit have been remediated and verified.

1. **Entitlement Enforcement Gaps**: FIXED. Enforcement now occurs after view resolution and correctly checks all physical tables in joins and CTEs.
2. **Parser Joins Preservation**: FIXED. `SharqParser` now correctly carries joins through all statement reconstruction paths.
3. **Execution Routing**: FIXED. `CompoundQueryExecutor` now correctly routes join-bearing intents to `JoinExecutor`.
4. **Join Predicate Evaluation**: FIXED. `JoinExecutor` now supports a wider range of operators (`BETWEEN`, `IN`, `LIKE`, etc.) and string operations.
5. **Alias Handling**: FIXED. `SharcDatabase` now supports automatic alias stripping for non-join queries.
6. **RIGHT JOIN Protection**: FIXED. `RIGHT JOIN` is now explicitly blocked with a `NotSupportedException`.
7. **Debug Logging Removal**: FIXED. All `Console.WriteLine` debug statements have been removed from core paths.

## Verification Snapshot (Final)
1. `dotnet build src/Sharc/Sharc.csproj` - PASSED.
2. `dotnet test tests/Sharc.Tests/Sharc.Tests.csproj` - PASSED (1241/1241 tests).
3. `EntitlementGapTests.cs` - PASSED (verified permission blockers for joins and views).

render_diffs(file:///c:/Code/Sharc/src/Sharc/SharcDatabase.cs)
render_diffs(file:///c:/Code/Sharc/src/Sharc/Query/TableReferenceCollector.cs)
render_diffs(file:///c:/Code/Sharc/src/Sharc.Query/Sharq/SharqParser.cs)
render_diffs(file:///c:/Code/Sharc/src/Sharc/Query/Execution/JoinExecutor.cs)
render_diffs(file:///c:/Code/Sharc/src/Sharc/Query/IntentToFilterBridge.cs)

