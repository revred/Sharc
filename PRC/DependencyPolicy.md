# Dependency Policy — Sharc

## 1. Core Principle

Sharc minimizes external dependencies. Every dependency is a supply-chain risk, a versioning constraint, and a maintenance burden. Dependencies are allowed only when the alternative is significantly worse.

## 2. Allowed Dependencies

### Production Libraries (src/)

| Package | Project | Purpose | Justification |
|---------|---------|---------|---------------|
| _(none currently)_ | Sharc | — | Zero-dependency public API |
| _(none currently)_ | Sharc.Core | — | Zero-dependency internals |
| `Konscious.Security.Cryptography` (future) | Sharc.Crypto | Argon2id KDF | .NET has no built-in Argon2. This is the most mature managed implementation. |

**`System.Security.Cryptography`** (built-in): Used for AES-GCM. Not a NuGet dependency — ships with the runtime.

### Test Libraries (tests/)

| Package | Purpose |
|---------|---------|
| `xunit` | Test framework |
| `xunit.runner.visualstudio` | Test runner for `dotnet test` |
| `Microsoft.NET.Test.Sdk` | Test infrastructure |
| `FluentAssertions` | Readable assertion syntax |

### Benchmark Libraries (bench/)

| Package | Purpose |
|---------|---------|
| `BenchmarkDotNet` | Performance measurement and profiling |

### Test-Only Utilities (allowed in test projects)

| Package | Purpose |
|---------|---------|
| `Microsoft.Data.Sqlite` | Generate real SQLite test databases |
| `SQLitePCLRaw.bundle_e_sqlite3` | Transitive dep of Microsoft.Data.Sqlite |

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

## 4. Evaluation Criteria for New Dependencies

Before adding any dependency to production code, answer ALL of these:

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
4. Pin the exact version in the `.csproj` file
5. Add a comment in the `.csproj` explaining why the dependency exists

```xml
<!-- Argon2id KDF — .NET has no built-in Argon2. See ADR-001. -->
<PackageReference Include="Konscious.Security.Cryptography" Version="1.1.7" />
```

## 7. Version Pinning

All NuGet package references use exact versions (no floating/wildcard):

```xml
<!-- ✓ Exact version -->
<PackageReference Include="xunit" Version="2.6.6" />

<!-- ✗ Floating version -->
<PackageReference Include="xunit" Version="2.*" />
```

This ensures reproducible builds and prevents surprise breaking changes.

## 8. License Compliance

All dependencies must use one of:
- MIT License
- Apache License 2.0
- BSD 2-Clause or 3-Clause
- MS-PL (Microsoft Public License)

**Not allowed**: GPL, LGPL, AGPL, SSPL, or any copyleft license that would affect Sharc's licensing.
