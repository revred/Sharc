# Entitlement vs Access Security Audit

Date: 2026-02-18
Branch: `WriteLeft` (live/in-progress)

## Scope
Audit of authorization and enforcement boundaries across:
- query/read access (`SharcDatabase.Query*`, query planning/execution)
- write access (`SharcWriter`, `SharcWriteTransaction`)
- schema/DDL access (`Transaction.Execute`, `SharcSchemaWriter`)
- trust/agent plumbing (`AgentInfo`, `EntitlementEnforcer`, `AgentRegistry`)
- privileged/raw APIs that can bypass policy

## Verification Snapshot
1. `dotnet test tests/Sharc.Tests/Sharc.Tests.csproj -c Release --nologo --filter "FullyQualifiedName~EntitlementGapTests.View_With_Join_Gap_Accesses_Denied_Table"` passes.
2. `dotnet test tests/Sharc.Tests/Sharc.Tests.csproj -c Release --nologo --filter "FullyQualifiedName~EntitlementGapTests.Join_Gap_Accesses_Denied_Table"` fails due to temp file lock at cleanup, but assertion flow indicates the entitlement gap remains relevant for simple JOIN collection.

## Findings (Ordered by Severity)
1. Critical: simple JOIN table coverage gap in entitlement collection.
   - Evidence:
     - Query path routes JOIN/Cote/compound plans to `EnforceAll` (`src/Sharc/SharcDatabase.cs:344`).
     - `TableReferenceCollector` simple branch only adds base table (`src/Sharc/Query/TableReferenceCollector.cs:25`).
     - JOIN tables are only added in `CollectFromIntent`, which is used for compound branches (`src/Sharc/Query/TableReferenceCollector.cs:41`).
   - Impact: a simple query with joins can be authorized against an incomplete table set.
   - Recommendation: in `Collect(QueryPlan)`, route all simple intents through `CollectFromIntent` instead of direct base-table add.

2. Critical: column-level read scope is bypassable for non-projection access.
   - Evidence:
     - Read enforcement checks projected columns only (`src/Sharc/SharcDatabase.cs:352`, `src/Sharc/Query/TableReferenceCollector.cs:43`).
     - Joined tables currently pass `null` columns (`src/Sharc/Query/TableReferenceCollector.cs:57`).
     - `EntitlementEnforcer` skips column checks when `columns == null` (`src/Sharc/Trust/EntitlementEnforcer.cs:86`).
   - Impact: columns referenced in `WHERE`, `JOIN ON`, `ORDER BY`, `GROUP BY`, `HAVING` are not reliably covered by column-level entitlements.
   - Recommendation: collect and enforce full referenced-column sets per table (not just output projection).

3. High: agent-aware write APIs bypass column-level write restrictions.
   - Evidence:
     - `Insert/InsertBatch/Update` call `EnforceWrite(..., null)` (`src/Sharc/SharcWriter.cs:103`, `src/Sharc/SharcWriter.cs:139`, `src/Sharc/SharcWriter.cs:206`).
     - `null` columns disable per-column checks (`src/Sharc/Trust/EntitlementEnforcer.cs:86`).
   - Impact: write-scope column restrictions are not enforced for common agent write operations.
   - Recommendation: derive target column names from `ColumnValue[]` payload and pass explicit column list to `EnforceWrite`.

4. High: explicit write transactions have no entitlement hook for DML.
   - Evidence:
     - `SharcWriter.BeginTransaction()` returns `SharcWriteTransaction` without binding an agent (`src/Sharc/SharcWriter.cs:218`).
     - `SharcWriteTransaction.Insert/Delete/Update` have no agent parameter and no entitlement checks (`src/Sharc/SharcWriteTransaction.cs:30`, `src/Sharc/SharcWriteTransaction.cs:41`, `src/Sharc/SharcWriteTransaction.cs:52`).
   - Impact: callers can bypass policy by using explicit transaction APIs.
   - Recommendation: add agent-bound transaction variant (or require per-operation agent) and enforce write scopes in all DML methods.

5. High: authorization trusts caller-supplied `AgentInfo` without identity verification.
   - Evidence:
     - Query/write checks accept provided `AgentInfo` and enforce scope fields directly (`src/Sharc/SharcDatabase.cs:331`, `src/Sharc/SharcWriter.cs:103`).
     - Signature/identity verification is implemented in registry/ledger workflows, not query/write gates (`src/Sharc/Trust/AgentRegistry.cs:43`, `src/Sharc/Trust/LedgerManager.cs:71`).
   - Impact: in untrusted-call environments, a forged in-memory `AgentInfo` can claim expanded scope.
   - Recommendation: add pluggable identity validation (registry-backed or signature-backed) before entitlement checks for privileged APIs.

6. Medium: privileged no-agent/raw APIs bypass entitlement model.
   - Evidence:
     - No-agent query overloads (`src/Sharc/SharcDatabase.cs:275`, `src/Sharc/SharcDatabase.cs:284`).
     - Direct table reader (`src/Sharc/SharcDatabase.cs:214`).
     - Raw b-tree reader exposed (`src/Sharc/SharcDatabase.cs:137`).
     - Raw page writer exposed (`src/Sharc/SharcDatabase.cs:755`).
   - Impact: applications can unintentionally bypass policy unless they wrap APIs externally.
   - Recommendation: document these as privileged APIs and optionally gate with a strict mode that disables them in managed deployments.

7. Medium: validity timestamp unit mismatch risk.
   - Evidence:
     - `AgentInfo` docs describe epoch milliseconds (`src/Sharc.Core/Trust/AgentInfo.cs:12`).
     - Enforcer compares against epoch seconds (`src/Sharc/Trust/EntitlementEnforcer.cs:67`).
   - Impact: misconfigured validity windows may be accepted/rejected incorrectly.
   - Recommendation: standardize units (prefer seconds or milliseconds consistently) and enforce via type-level naming or validation.

## Strengths Confirmed
1. View resolution now runs before entitlement checks in query flow (`src/Sharc/SharcDatabase.cs:327` then `src/Sharc/SharcDatabase.cs:330`), which is directionally correct.
2. `TableReferenceCollector` now handles JOIN tables for intents that route through `CollectFromIntent` (`src/Sharc/Query/TableReferenceCollector.cs:45`).
3. Schema DDL path enforces schema-admin scope when agent is provided (`src/Sharc/SharcSchemaWriter.cs:24`).

## Recommended Remediation Order
1. Fix simple JOIN table collection path (critical authorization coverage).
2. Enforce column-level authorization on full query/write references, not projection only.
3. Add entitlement enforcement to explicit transaction DML APIs.
4. Add identity verification hook for `AgentInfo` on privileged entry points.
5. Gate/document privileged no-agent/raw APIs for managed deployments.
6. Normalize validity timestamp units and add guard tests.

## Regression Tests To Add
1. Simple JOIN entitlement: denied joined table must throw (non-compound query).
2. Column-scope query test: deny access to columns used only in predicate/order/group/join.
3. Column-scope write test: deny update/insert of restricted columns for agent writes.
4. Transaction DML entitlement: agent restrictions enforced inside `SharcWriteTransaction`.
5. Forged `AgentInfo` negative test in managed mode (identity validation required).
6. Validity unit tests for seconds vs milliseconds boundaries.
