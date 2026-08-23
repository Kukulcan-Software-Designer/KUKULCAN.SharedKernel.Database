# KUKULCAN.SharedKernel.Database.Integration

This project contains integration tests for `KUKULCAN.SharedKernel.Database` against a real PostgreSQL server.

## Scope

The integration suite validates infrastructure behavior that cannot be fully represented by the unit-test suite, including:

- real PostgreSQL connectivity and persistence;
- EF Core provider configuration;
- tenant query isolation against a real database;
- soft-delete interception against real SQL persistence;
- audit timestamp persistence;
- real database transactions through `UnitOfWork<TContext>`.

The project intentionally does **not** reference or modify production code solely to improve code coverage.

## Running locally

A PostgreSQL instance must be available. The default connection string is:

```text
Host=localhost;Port=5432;Database=kukulcan_sharedkernel_database_integration;Username=postgres;Password=postgres
```

The connection string can be overridden with:

```text
KUKULCAN_DATABASE_INTEGRATION_CONNECTION_STRING
```

For example:

```bash
dotnet test Tests/KUKULCAN.SharedKernel.Database.Integration/KUKULCAN.SharedKernel.Database.Integration.csproj --configuration Release
```

## CI

GitHub Actions starts PostgreSQL 16 as a service container and runs this project independently from the unit-test and coverage workflows.

These tests are **integration tests, not coverage tests**. Coverlet is intentionally not referenced by this project and no coverage threshold is applied.
