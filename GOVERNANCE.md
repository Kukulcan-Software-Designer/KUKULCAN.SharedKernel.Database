# Governance

## Purpose

`KUKULCAN.SharedKernel.Database` is governed as shared persistence infrastructure. Decisions must protect stability, low coupling and predictable behavior across consuming modules.

## Principles

- **Single responsibility:** database infrastructure only.
- **Low coupling:** consume SharedKernel contracts instead of duplicating domain abstractions.
- **Explicitness:** persistence behavior should be visible in configuration or named infrastructure components.
- **Stability:** public contracts change only for justified reasons.
- **Provider neutrality:** provider packages remain outside the core library.

## Decision Process

1. Identify the repeated persistence problem.
2. Confirm that it is shared by multiple modules.
3. Decide whether it belongs in SharedKernel or Database.
4. Review API and compatibility impact.
5. Implement, test and document.

## Frozen Components

Stable modules and contracts should not be changed without evidence of a real requirement, compatibility defect or architectural improvement.

## Public API Policy

New public types require a clear consumer-facing purpose. Implementation details remain private whenever possible.

## Quality Gates

Changes should compile with warnings treated as errors, satisfy XML documentation requirements and include appropriate behavior-focused tests.

Pull requests targeting `main` should require the following GitHub Actions checks before merge:

- `CI / Restore, build and test Database`
- `Reference Client / Build KUKULCAN.SharedKernel.Database.Client`
- `Coverage / ...` when the coverage workflow is part of the required check set
- the provider-specific integration checks for SQL Server, PostgreSQL and MySQL when they are configured as required checks

The `main` branch should be configured in GitHub with branch protection or an equivalent ruleset so that direct merges cannot bypass these quality gates.
