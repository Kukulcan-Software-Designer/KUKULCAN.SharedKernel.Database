# Testing Strategy

## Testing Philosophy

Tests for `KUKULCAN.SharedKernel.Database` should verify actual persistence behavior rather than create artificial coverage.

The most valuable tests exercise:

- EF Core model metadata;
- query filters;
- SaveChanges interception;
- transaction behavior;
- dependency injection registration;
- domain event dispatch;
- audit timestamp updates;
- immutable entity enforcement;
- slow-query logging.

## Test Projects

The repository separates deterministic unit tests from real PostgreSQL persistence validation:

```text
Tests/
├── KUKULCAN.SharedKernel.Database.Tests/
│   └── Unit tests and production coverage collection
└── KUKULCAN.SharedKernel.Database.Integration/
    └── PostgreSQL-backed integration tests
```

### Unit Tests

`KUKULCAN.SharedKernel.Database.Tests` is responsible for deterministic behavior that does not require a real database server.

Typical examples include:

- null argument validation;
- option defaults and configuration binding;
- provider-selection errors;
- provider reflection and configuration logic;
- `UnitOfWork<TContext>` argument contracts;
- interceptor synchronous and asynchronous entry points;
- EF Core model metadata and model-builder behavior.

The unit-test project references the EF Core InMemory and SQLite providers, as well as SQL Server and PostgreSQL provider packages where provider-specific configuration paths need to be exercised with the real extension assemblies.

### Integration Tests

`KUKULCAN.SharedKernel.Database.Integration` uses **PostgreSQL as the reference database management system (DBMS)**.

The integration layer validates behavior that depends on a real relational provider, including:

- PostgreSQL connectivity and persistence;
- tenant isolation against real database rows;
- tenant model-cache isolation across contexts;
- audit and soft-delete persistence;
- domain-event dispatch after successful persistence;
- immutable-entity enforcement;
- slow-query diagnostics against real database commands;
- transaction, cancellation and rollback behavior.

The integration project uses `Testcontainers.PostgreSql` for isolated PostgreSQL test infrastructure and can collect coverage data through `coverlet.collector`. Integration coverage is diagnostic only; the accepted deterministic coverage baseline is defined by the unit-test project.

## Recommended Test Layers

``` mermaid
flowchart TD
    U["Unit tests"] --> I["Infrastructure integration tests"]
    I --> EF["EF Core model / ChangeTracker"]
    EF --> DB["PostgreSQL provider"]
```

## EF Core Tests

Behavior depending on ChangeTracker or model metadata should use a real EF Core model.

Examples:

- soft-delete filters;
- tenant filters;
- immutable interception;
- auditing;
- domain events;
- tenant-aware model cache behavior.

## SQLite

SQLite in-memory is useful for relational behavior that can be validated without a database server because EF Core's in-memory provider does not model relational transactions faithfully.

## Provider Tests

Provider-specific behavior should be tested in provider-aware test environments when the provider packages are actually consumed.

For this repository, **PostgreSQL is the reference DBMS for integration testing**. The shared library should not pretend that provider-specific behavior is completely equivalent across engines.

## Coverage Policy

Unit-test coverage is inspected at class and method level through the generated Cobertura report. The current accepted baseline is:

- **100% line coverage**;
- **97.36% branch coverage**.

The remaining defensive provider-resolution branches are documented in `COVERAGE.md` and are not forced through artificial runtime conditions solely to reach 100% branch coverage.

## Obsolete API Policy

Tests should use the current EF Core metadata APIs.

For example:

```csharp
GetDeclaredQueryFilters()
```

should be preferred over obsolete:

```csharp
GetQueryFilter()
```

This keeps the test suite aligned with EF Core 10.
