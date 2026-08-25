# Reference Client

`KUKULCAN.SharedKernel.Database.Client` is the executable reference client for `KUKULCAN.SharedKernel.Database`.

## Providers

The same executable scenario suite is selected at runtime for:

- SQL Server — `Microsoft.EntityFrameworkCore.SqlServer`
- PostgreSQL — `Npgsql.EntityFrameworkCore.PostgreSQL`
- MySQL — `MySql.EntityFrameworkCore`

No scenario contains provider-specific EF Core code.

## Exhaustive reference scenarios

The `Full Reference Client` mode executes the same cases for the selected provider:

1. Provider/configuration validation.
2. `IUnitOfWork.SaveChangesAsync`.
3. `BeginTransactionAsync` + `CommitTransactionAsync`.
4. `BeginTransactionAsync` + `RollbackTransactionAsync`.
5. `BeginTransactionAsync` + `EndTransactionAsync`.
6. Database-command cancellation.
7. Tenant-aware model cache key across independent `DbContext` instances.
8. Migration path (`MigrateAsync` when migrations exist) with `EnsureCreatedAsync` fallback for a migration-less demo database, plus idempotent seed data.
9. Provider execution strategy / retry configuration.
10. Audit interceptor.
11. Soft-delete interceptor and global filter.
12. Immutable entity interceptor.
13. Domain-event dispatch interceptor.
14. Slow-query interceptor path.
15. Tenant global filter.

The suite fails fast on the first failing scenario and reports a final pass count.

## Provider-neutral model rules

The client model deliberately avoids SQL Server/PostgreSQL-only schema constructs. In particular, it does not configure `HasDefaultSchema`, because MySQL does not provide an equivalent schema namespace. Tables are mapped by neutral table names only.

## Database initialization

Startup uses `ClientDatabaseInitializer`:

- If the client context exposes migrations, pending migrations are applied with `MigrateAsync`.
- If the context has no migrations, `EnsureCreatedAsync` creates the demonstration schema.
- Deterministic seed data is then inserted idempotently.

This keeps startup identical for all three providers while still exercising the migration API when a migration assembly is supplied.

## Running the exhaustive suite

Start the client and select:

`Full Reference Client — ejecutar todos los casos de uso`

The connection strings are read from `appsettings.json` under `Providers:PostgreSql`, `Providers:SqlServer`, and `Providers:MySql`, and may be overridden through environment variables.
