# Dependency Policy — Sharc

## 1. Core Principle

Sharc's core library ships with **zero external NuGet dependencies**. This is a competitive advantage and a non-negotiable project value. Every dependency is a supply-chain risk, a versioning constraint, and a maintenance burden. Dependencies are allowed only when the alternative is significantly worse — and only with explicit approval.

## 2. Allowed Dependencies

### Production Libraries (src/)

| Package | Project | Purpose | Justification |
|---------|---------|---------|---------------|
| _(none currently)_ | Sharc | — | Zero-dependency public API |
| _(none currently)_ | Sharc.Core | — | Zero-dependency internals |
| _(none currently)_ | Sharc.Query | — | Zero-dependency SQL pipeline |
| _(none currently)_ | Sharc.Crypto | — | Zero-dependency encryption |
| _(none currently)_ | Sharc.Graph | — | Zero-dependency graph engine |
| _(none currently)_ | Sharc.Graph.Surface | — | Zero-dependency graph models |

**`System.Security.Cryptography`** (built-in): Used for AES-GCM. Not a NuGet dependency — ships with the runtime.

### Test Libraries (tests/)

| Package | Purpose |
|---------|---------|
| `xunit` | Test framework |
| `xunit.runner.visualstudio` | Test runner for `dotnet test` |
| `Microsoft.NET.Test.Sdk` | Test infrastructure |

### Benchmark Libraries (bench/)

| Package | Purpose |
|---------|---------|
| `BenchmarkDotNet` | Performance measurement and profiling |

### Test & Benchmark Utilities (allowed in test/bench projects only)

| Package | Purpose |
|---------|---------|
| `Microsoft.Data.Sqlite` | Generate real SQLite databases for comparison |
| `SQLitePCLRaw.bundle_e_sqlite3` | Transitive dep of Microsoft.Data.Sqlite |

### Arena (Blazor WASM app — `src/Sharc.Arena.Wasm/`)

| Package | Purpose | Justification |
|---------|---------|---------------|
| `Microsoft.AspNetCore.Components.WebAssembly` | Blazor runtime | Required for WASM app |
| `Microsoft.AspNetCore.Components.WebAssembly.DevServer` | Dev server | PrivateAssets="all", dev-only |
| `Microsoft.Data.Sqlite` | SQLite comparison engine | Baseline for Arena benchmarks |
| `SQLitePCLRaw.bundle_e_sqlite3` | Native SQLite WASM | Required for native relinking |

### Tools (`tools/`)

| Package | Project | Purpose |
|---------|---------|---------|
| `ModelContextProtocol` | Sharc.Context | MCP server framework |
| `Microsoft.Extensions.Hosting` | Sharc.Context | Host builder |
| `Microsoft.Data.Sqlite` | Sharc.Index, Sharc.Debug | SQLite operations |

### Development Support

| Package | Purpose |
|---------|---------|
| `Microsoft.SourceLink.GitHub` | Embeds source info in PDBs for NuGet debugger support |

## 3. Prohibited Dependencies

These are explicitly **not allowed** in production code:

| Category | Examples | Reason |
|----------|----------|--------|
| JSON libraries | Newtonsoft.Json, System.Text.Json | Sharc doesn't use JSON |
| DI containers | Autofac, Microsoft.Extensions.DI | No dependency injection; composition is manual |
| Logging frameworks | Serilog, NLog, Microsoft.Extensions.Logging | Library code should not log; consumers decide logging |
| ORM / data access | EF Core, Dapper | Sharc IS the data access layer |
| HTTP clients | HttpClient, RestSharp | No network access |
| Compression | SharpZipLib | Not needed for SQLite format |
| Polly / resilience | Polly | No retry logic in a file reader |
| Alternative test frameworks | MSTest, NUnit | Standardized on xunit; no mixing |
| Assertion libraries | FluentAssertions, Shouldly | Standardized on plain `Assert.*`; no mixing |
| Heavy BCL bloatware | System.Linq.Expressions, System.Reflection.Emit | ~400 KB each; fully eliminated — FilterStar uses closure-based delegate composition instead |

## 4. Evaluation Criteria for New Dependencies

Before adding any dependency to **any project**, answer ALL of these:

1. **Is there a built-in alternative?** (System.* namespaces, BCL) → Use it
2. **Can we implement it in <200 lines?** → Implement it ourselves
3. **Is the package actively maintained?** (commits in last 6 months)
4. **Does it have a clear, permissive license?** (MIT, Apache-2.0, BSD)
5. **Does it pull in transitive dependencies?** → Prefer packages with zero transitives
6. **Is it used by >10K packages on NuGet?** (proxy for community trust)
7. **Does it affect our binary size by >100 KiB?** → Reconsider

A dependency must satisfy ALL criteria to be accepted.

## 5. BCL Usage Guidelines

Prefer BCL types and methods:

| Need | Use | Not |
|------|-----|-----|
| Big-endian reads | `BinaryPrimitives` | Manual bit shifting |
| Array pooling | `ArrayPool<byte>.Shared` | Custom pool |
| Hashing | `System.Security.Cryptography` | BouncyCastle |
| AES encryption | `System.Security.Cryptography.AesGcm` | Third-party AES |
| Span operations | `MemoryExtensions` | Custom helpers |
| File I/O | `FileStream`, `RandomAccess`, `MemoryMappedFile` | — |
| UTF-8 encoding | `Encoding.UTF8` | Custom UTF-8 decoder |
| Concurrent collections | `ConcurrentDictionary` | Custom locking |

## 6. Dependency Review Process

1. Open an issue titled `dep: add [package-name]`
2. Document the evaluation against all 7 criteria
3. Record the decision in `PRC/DecisionLog.md`
4. Pin the exact version in `Directory.Packages.props` (central management)
5. Add a comment in the `.csproj` explaining why the dependency exists

```xml
<!-- Argon2id KDF — .NET has no built-in Argon2. See ADR-001. -->
<PackageReference Include="Konscious.Security.Cryptography" />
```

## 7. Version Pinning

All NuGet package versions are centrally managed in `Directory.Packages.props`:

```xml
<!-- ✓ Central version management -->
<PackageVersion Include="xunit" Version="2.6.6" />

<!-- ✗ Version in .csproj (breaks central management) -->
<PackageReference Include="xunit" Version="2.6.6" />
```

This ensures reproducible builds and a single source of truth for all versions.

## 8. License Compliance

All dependencies must use one of:
- MIT License
- Apache License 2.0
- BSD 2-Clause or 3-Clause
- MS-PL (Microsoft Public License)

**Not allowed**: GPL, LGPL, AGPL, SSPL, or any copyleft license that would affect Sharc's licensing.

## 9. Periodic Audit

After every major feature branch merge:

1. Run `dotnet list package` across the solution
2. Verify every `PackageVersion` in `Directory.Packages.props` is actually referenced by at least one `.csproj`
3. Remove any orphan entries (packages declared but never consumed)
4. Check for newer versions with `dotnet list package --outdated`
5. Any new dependency requires a `dep:` prefixed commit message and DecisionLog entry

## 10. Approval Gate

- **No new `PackageReference` or `PackageVersion` may be added without explicit user approval**
- AI assistants must present the 7-criteria evaluation BEFORE adding any dependency
- This applies to ALL projects: `src/`, `tests/`, `bench/`, `tools/`
- Removing unused dependencies does not require approval — cleanup is always welcome
