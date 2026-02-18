# WriteLeft Findings And Recommendations

Date: 2026-02-18
Branch: `WriteLeft` (live/in-progress)

## Verification Snapshot
1. `dotnet build src/Sharc/Sharc.csproj -c Release --nologo` passes.
2. `dotnet test tests/Sharc.Tests/Sharc.Tests.csproj -c Release --nologo --filter "FullyQualifiedName~JoinTests|FullyQualifiedName~ViewTests"` passes (10/10).

## Open Findings (Current)
1. Critical: entitlement enforcement gaps remain for joins and views.
   - Evidence: enforcement runs before view rewrite (`src/Sharc/SharcDatabase.cs:327` before `src/Sharc/SharcDatabase.cs:344`).
   - Evidence: non-compound path enforces only the base table (`src/Sharc/SharcDatabase.cs:338`).
   - Evidence: table collection omits joined tables in simple and compound paths (`src/Sharc/Query/TableReferenceCollector.cs:26`, `src/Sharc/Query/TableReferenceCollector.cs:33`, `src/Sharc/Query/TableReferenceCollector.cs:38`).
   - Impact: joined or view-expanded base tables can bypass intended read-scope checks.
   - Recommendation: enforce after view resolution and collect all physical table references including join tables; use `EnforceAll` for any multi-table plan.

2. High: joins are still dropped in parser wrapper/rebuild paths.
   - Evidence: WITH wrapper rebuild at `src/Sharc.Query/Sharq/SharqParser.cs:75` to `src/Sharc.Query/Sharq/SharqParser.cs:89` does not set `Joins`.
   - Evidence: compound rebuild at `src/Sharc.Query/Sharq/SharqParser.cs:142` to `src/Sharc.Query/Sharq/SharqParser.cs:155` does not set `Joins`.
   - Impact: joins can disappear when statements are wrapped/reconstructed.
   - Recommendation: carry `Joins = stmt.Joins` and `Joins = left.Joins` in all reconstructed `SelectStatement` instances.

3. High: compound/Cote execution still bypasses join execution path.
   - Evidence: `src/Sharc/Query/CompoundQueryExecutor.cs:562` to `src/Sharc/Query/CompoundQueryExecutor.cs:568` always uses `CreateReaderFromIntent` + `QueryPostProcessor.Apply`, without `JoinExecutor` routing.
   - Impact: joins inside compound/Cote subqueries may execute incorrectly or be ignored.
   - Recommendation: route `intent.HasJoins` to `JoinExecutor.Execute` in `ExecuteIntent`.

4. High: join filter evaluator is incomplete and has unsafe comparison fallback.
   - Evidence: custom evaluator path in `src/Sharc/Query/Execution/JoinExecutor.cs:244` onward only handles a subset of operators in `Compare` (`src/Sharc/Query/Execution/JoinExecutor.cs:285` to `src/Sharc/Query/Execution/JoinExecutor.cs:306`).
   - Evidence: `ResultCompare` falls back to `return 0` for unsupported type combinations (`src/Sharc/Query/Execution/JoinExecutor.cs:334`).
   - Impact: predicates such as `IN`, `LIKE`, `BETWEEN`, parameterized cases, or mixed-type comparisons can produce incorrect results.
   - Recommendation: reuse existing predicate evaluation machinery or throw for unsupported operators/types instead of silent fallback.

5. Medium: RIGHT JOIN semantics are not fully implemented.
   - Evidence: current hash join loop only preserves unmatched left rows and contains explicit partial-right-join comments (`src/Sharc/Query/Execution/JoinExecutor.cs:158` to `src/Sharc/Query/Execution/JoinExecutor.cs:166`).
   - Impact: `RIGHT JOIN` can return incorrect row sets.
   - Recommendation: fully implement right-outer behavior or reject `RIGHT JOIN` until complete.

6. Medium: alias-qualified column names likely break non-join scans.
   - Evidence: compiler now emits qualified refs via `FormatColumnRef` (`src/Sharc.Query/Intent/IntentCompiler.cs:353`, `src/Sharc.Query/Intent/IntentCompiler.cs:412`).
   - Evidence: non-join projection resolves against physical column names via `GetColumnOrdinal` (`src/Sharc/SharcDatabase.cs:494`).
   - Impact: non-join alias queries such as `SELECT u.name FROM users u` can fail with column not found.
   - Recommendation: normalize aliases when `HasJoins == false`, or make non-join projection/filter resolution alias-aware.

7. Medium: debug logging is still committed in parser runtime path.
   - Evidence: `Console.WriteLine` calls remain at `src/Sharc.Query/Sharq/SharqParser.cs:170` and `src/Sharc.Query/Sharq/SharqParser.cs:171`.
   - Impact: noisy stdout and possible leakage in consumers.
   - Recommendation: remove or gate behind explicit diagnostics logging.

## Previously Fixed / Improved
1. View resolution activation was fixed by resolving views via `GetSchema().GetView(...)` in `src/Sharc/SharcDatabase.cs:384`.
2. View expansion now includes dependency ordering (topological traversal) in `src/Sharc/SharcDatabase.cs:401` to `src/Sharc/SharcDatabase.cs:434`.
3. `SharcDataReader` constructor complexity was reduced via `CursorReaderConfig`.
4. Cote compile model now supports full `QueryPlan` (`src/Sharc.Query/Intent/CoteIntent.cs`), enabling non-simple Cote definitions.
5. Cote inlining now explicitly blocks join-bearing Cotes (`src/Sharc/Query/CompoundQueryExecutor.cs:120`).
6. Join parsing in direct `SELECT` path now attaches joins (`src/Sharc.Query/Sharq/SharqParser.cs:209`).
7. Join and view tests are currently green (10/10 in filtered run).

## Recommended Fix Order
1. Close entitlement gaps for joins/views (post-resolution enforcement + full table collection).
2. Preserve joins in parser rebuild wrappers.
3. Route joins through compound/Cote `ExecuteIntent` path.
4. Replace or complete join predicate evaluation.
5. Implement or block `RIGHT JOIN`.
6. Resolve alias-qualified non-join column handling.
7. Remove parser debug logging.

## Regression Tests To Add / Keep
1. Entitlement: unauthorized second table in join must throw.
2. Entitlement: view query must enforce underlying table scopes, not only view name.
3. Parser: WITH + JOIN and compound + JOIN retain joins end-to-end.
4. Compound/Cote execution: join-bearing subqueries route through join execution.
5. Join predicate matrix: parameterized, `LIKE`, `IN`, `BETWEEN`, null semantics.
6. Non-join alias: `SELECT u.name FROM users u` must work.
7. RIGHT JOIN: either semantic correctness tests or explicit `NotSupported` assertion.
