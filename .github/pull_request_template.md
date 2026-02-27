# Summary

Describe what changed and why.

# Validation

- [ ] `dotnet build --configuration Release`
- [ ] `dotnet test --configuration Release`

# Release Checklist (Required for PRs into `main`)

- [ ] Updated `README.md` with user-facing package/API changes
- [ ] Added release notes in `CHANGELOG.md` under `## [1.2.<PR_NUMBER>] - YYYY-MM-DD`
- [ ] Verified NuGet publish workflow can pack all required packages (`Sharc.Query`, `Sharc.Core`, `Sharc.Crypto`, `Sharc.Graph.Surface`, `Sharc`, `Sharc.Graph`, `Sharc.Vector`, `Sharc.Arc`)
