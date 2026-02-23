# Commit Checklist

What to verify and update before committing changes to the Sharc repository.

---

## 1. Tests

- [ ] `dotnet test` — all tests pass (currently 2,660+ across 6 projects)
- [ ] `dotnet build` — zero errors, zero warnings (except known CA1861 in test code)
- [ ] New code has corresponding test(s) (TDD: red → green → refactor)

## 2. Benchmark Numbers

If benchmark numbers changed, update **all** of these:

| File | What to update |
|------|----------------|
| `README.md` | Headline numbers table (line ~123), headline claims (line ~16), Arena table (line ~186), comparison table (line ~237) |
| `docs/BENCHMARKS.md` | Graph Storage table (line ~44), summary note, Sharc vs SQLite comparison table, last-run date (line ~6) |
| `docs/FAQ.md` | Any speedup claims (search for `x faster`) |
| `docs/WHEN_NOT_TO_USE.md` | "Why Sharc Wins" table |
| `docs/ARCHITECTURE.md` | "Why Sharc Is Fast" optimization table |
| `src/Sharc.Graph/NuGet_README.md` | Graph traversal speedup claim |
| `src/Sharc.Arena.Wasm/wwwroot/data/graph-benchmarks.json` | Arena benchmark metadata |
| `CHANGELOG.md` | Performance claims in feature descriptions |

**Quick search:** `grep -r "13.5x\|6.04 us\|old_number" --include="*.md"` to find stale references.

## 3. Test Counts

If test count changed, update:

| File | Section |
|------|---------|
| `README.md` | Badge (line ~7), Build & Test comment, Project Structure test counts |
| `CLAUDE.md` | Current Status section, Project Structure test counts |
| `CONTRIBUTING.md` | "All N tests must pass" requirement |
| `PRC/ArchitectureOverview.md` | Current Test Status section |
| `PRC/TestStrategy.md` | CI Requirements section |
| `docs/FAQ.md` | "Is this production-ready?" answer |
| `CHANGELOG.md` | Infrastructure test count |

**Note:** `PRC/DecisionLog.md` records historical counts at decision time — do NOT update those.

**Quick search:** `grep -rn "tests passing\|tests across\|tests must pass" --include="*.md"` to find all references.

## 4. API Changes

If public API changed (new methods, deprecations, signature changes):

| File | What to update |
|------|----------------|
| `wiki/AI-Agent-Reference.md` | Copy-paste patterns, critical rules, common mistakes |
| `wiki/` (relevant page) | API reference for the affected area |
| `docs/GETTING_STARTED.md` | Pattern examples using affected API |
| `docs/COOKBOOK.md` | Recipes using affected API |
| `src/Sharc.Graph/NuGet_README.md` | NuGet package readme (if graph API changed) |

## 5. Architecture Changes

If architecture changed (new layers, moved code, new projects):

| File | What to update |
|------|----------------|
| `CLAUDE.md` | Architecture diagram, namespace conventions, project structure |
| `docs/ARCHITECTURE.md` | Mermaid diagram, component breakdown table, project structure |
| `PRC/ArchitectureOverview.md` | Milestone descriptions, implementation details |
| `README.md` | Project Structure section |

## 6. New Dependencies

- [ ] Check `PRC/DependencyPolicy.md` before adding
- [ ] Update `Directory.Packages.props` (central package management)
- [ ] Document rationale in `PRC/DecisionLog.md`

## 7. Pre-Push

- [ ] `dotnet build` succeeds
- [ ] `dotnet test` all green
- [ ] No secrets/credentials in staged files (`.env`, keys, tokens)
- [ ] Commit message follows convention: `Category: Short description`
- [ ] No `--no-verify` or `--force` flags used

---

## Quick Validation Script

```bash
# Build
dotnet build

# Test
dotnet test

# Find stale benchmark numbers (adjust pattern as needed)
grep -rn "13.5x\|6.04 us\|12.5x" --include="*.md" | grep -v DecisionLog

# Find stale test counts
grep -rn "2,038\|1,229\|293 " --include="*.md" | grep -v DecisionLog
```
