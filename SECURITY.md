# Security Policy

## Supported Versions

| Version | Supported |
| :--- | :--- |
| 1.0.x-beta | Yes |

## Reporting a Vulnerability

If you discover a security vulnerability in Sharc, **please report it responsibly**.

1. **Do not** open a public GitHub issue.
2. Email **[security@sharc.dev](mailto:security@sharc.dev)** or use [GitHub's private vulnerability reporting](https://github.com/revred/Sharc/security/advisories/new).
3. Include:
   - Description of the vulnerability
   - Steps to reproduce
   - Impact assessment
   - Suggested fix (if any)

We will acknowledge your report within **48 hours** and aim to release a fix within **7 days** for critical issues.

## Scope

Sharc's security surface includes:

- **AES-256-GCM encryption** (Sharc.Crypto) — database-level encryption at rest
- **Argon2id key derivation** — password-based key stretching
- **ECDSA agent attestation** (Trust layer) — cryptographic identity for AI agents
- **Hash-chain ledger** — tamper-evident audit log

## Security Design Principles

- **No native dependencies** — pure managed C#, no P/Invoke attack surface
- **No `unsafe` code** unless profiling proves >20% gain
- **Span-based I/O** — no unnecessary buffer copies that could leak data
- **Deterministic builds** — reproducible from source
