# Reference Client

`KUKULCAN.SharedKernel.Database.Client` is the executable reference client for `KUKULCAN.SharedKernel.Database`.

## Providers

The same executable scenario suite is selected at runtime for:

- SQL Server — `Microsoft.EntityFrameworkCore.SqlServer`
- PostgreSQL — `Npgsql.EntityFrameworkCore.PostgreSQL`
- MySQL — `MySql.EntityFrameworkCore`

The scenario runner uses the same application code, model and persistence scenarios for all three providers. Provider selection is performed at startup; no scenario branches by database engine.

## Reference scenarios

The `Full Reference Client` mode executes the following cases for the selected provider:

1. Provider and client `DbContext` configuration.
2. `IUnitOfWork.SaveChangesAsync`.
3. `BeginTransactionAsync` + `CommitTransactionAsync`, including verification from a separate context.
4. `BeginTransactionAsync` + `RollbackTransactionAsync`, including verification from a separate context.
5. `BeginTransactionAsync` + `EndTransactionAsync`.
6. Database-command cancellation using an already-cancelled token.
7. Tenant-aware model creation across independent `DbContext` instances for two tenants.
8. Migration path: `MigrateAsync` when migrations are available, otherwise `EnsureCreatedAsync`, followed by idempotent reference seed data.
9. Provider execution strategy through `CreateExecutionStrategy()`.
10. Audit interceptor for insert and update timestamps.
11. Soft-delete interceptor and global soft-delete filter, including verification through `IgnoreQueryFilters()`.
12. Immutable entity interceptor for both update and delete attempts.
13. Domain-event dispatch interceptor and verification of the dispatched event.
14. Slow-query interceptor execution path.
15. Tenant global filter isolation between two tenants.

The suite is fail-fast: a scenario that throws stops the reference run. Successful scenarios print an individual `PASS` line. The current implementation does not maintain or print a final aggregate pass counter.

## Provider-neutral model rules

The client model deliberately avoids provider-specific schema constructs. In particular, it does not configure `HasDefaultSchema`; tables use neutral table names so the same EF Core model can be used with SQL Server, PostgreSQL and MySQL.

## Database initialization

Startup uses `ClientDatabaseInitializer` before the interactive or full-reference mode begins:

- `Database.GetMigrations()` checks whether the client context exposes EF Core migrations.
- When migrations exist, pending migrations are applied with `MigrateAsync`.
- When no migrations exist, `EnsureCreatedAsync` creates the demonstration schema.
- A reference `ClientProduct` seed row is then inserted idempotently when it is not already present.

The client currently does not ship a dedicated migrations assembly of its own, so a normal migration-less client database follows the `EnsureCreatedAsync` path. The migration code path remains available for a context supplied with migrations.

The seed is idempotent and uses a fixed reference name to detect whether it has already been inserted. The generated entity identifier itself is not deterministic because `ClientProduct.Create` generates a new `Guid`.

## Running the reference suite

Start the client and select:

`Full Reference Client — ejecutar todos los casos de uso`

The connection strings are read from `appsettings.json` under `Providers:PostgreSql`, `Providers:SqlServer`, and `Providers:MySql`. Environment variables are also loaded by the client configuration and can override configuration values.
