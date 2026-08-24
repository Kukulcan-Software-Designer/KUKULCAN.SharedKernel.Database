# KUKULCAN.SharedKernel.Database Documentation

This directory contains the technical documentation for the persistence infrastructure library.

## Architecture

- [`ARCHITECTURE.md`](ARCHITECTURE.md) — architectural boundary, dependency inversion and infrastructure responsibilities.
- [`ARCHITECTURE_DECISIONS.md`](ARCHITECTURE_DECISIONS.md) — accepted architectural decisions and their rationale.
- [`PERSISTENCE_PIPELINE.md`](PERSISTENCE_PIPELINE.md) — EF Core `SaveChanges` interception pipeline and ordering.

## Persistence Features

- [`AUDITING.md`](AUDITING.md) — audit timestamp behavior through `IAuditable` and `IClock`.
- [`SOFT_DELETE.md`](SOFT_DELETE.md) — logical deletion and global query filtering.
- [`TENANCY.md`](TENANCY.md) — tenant isolation and global tenant filtering.
- [`DOMAIN_EVENTS.md`](DOMAIN_EVENTS.md) — domain-event dispatch after successful persistence.
- [`IMMUTABILITY.md`](IMMUTABILITY.md) — append-only enforcement for `IImmutable` entities.
- [`UNIT_OF_WORK.md`](UNIT_OF_WORK.md) — persistence and explicit transaction lifecycle.

## Configuration

- [`CONFIGURATION.md`](CONFIGURATION.md) — `KukulcanDatabaseOptions`, provider selection, retry, pool, migration and diagnostic options.

## Testing and Coverage

- [`TESTING.md`](TESTING.md) — testing strategy and separation of deterministic tests from provider-backed validation.
- [`COVERAGE.md`](COVERAGE.md) — accepted unit-test coverage baseline and the rationale for the remaining defensive branches.

## Current Test Model

The repository contains two dedicated test projects:

```text
Tests/
├── KUKULCAN.SharedKernel.Database.Tests/
│   └── deterministic unit tests and code-coverage collection
└── KUKULCAN.SharedKernel.Database.Integration/
    └── PostgreSQL-backed integration tests
```

Unit tests are responsible for deterministic code-path and branch coverage. Integration tests use **PostgreSQL as the reference database management system (DBMS)** to validate real persistence behavior such as tenant isolation, model-cache isolation, interception and transactions.

The production database library remains provider-neutral at package level. Concrete providers are supplied by the consuming test or infrastructure project as required.
