# KUKULCAN.SharedKernel.Database

## Overview

`KUKULCAN.SharedKernel.Database` is the shared persistence and Entity Framework Core infrastructure module used by KUKULCAN applications and bounded contexts. It builds on `KUKULCAN.SharedKernel` and centralizes database cross-cutting concerns without introducing business-domain rules into the shared domain model.

The library targets **.NET 10** and **EF Core 10**. Concrete provider packages are deliberately supplied by consuming infrastructure or host projects rather than by the production database library itself.

## Responsibilities

- `KukulcanDbContextBase` for common EF Core configuration.
- SQL Server and PostgreSQL provider selection through `DatabaseProvider`.
- Strongly typed configuration through `KukulcanDatabaseOptions`.
- `IUnitOfWork` and `UnitOfWork<TContext>` for persistence and explicit transactions.
- Audit timestamp population through SharedKernel `IAuditable`.
- Soft-delete conversion and filtering through SharedKernel `ISoftDelete`.
- Persistence-level tenant isolation through `ITenantContext` and `TenantId`.
- Domain-event dispatch after successful persistence.
- Append-only enforcement through `IImmutable`.
- Slow-query diagnostics through `SlowQueryInterceptor`.
- Dependency-injection registration helpers.

## Architectural Boundary

This project is infrastructure, not a second domain kernel. It consumes stable contracts from `KUKULCAN.SharedKernel` and keeps persistence-only abstractions local. It must not contain bounded-context business rules, application services, CQRS handlers or generic repositories.

```text
KUKULCAN.SharedKernel
          ^
          |
KUKULCAN.SharedKernel.Database
          ^
          |
 consuming Infrastructure / Host applications
```

## Project Structure

```text
KUKULCAN.SharedKernel.Database/
├── Source/KUKULCAN.SharedKernel.Database/
│   ├── Abstractions/
│   ├── Configuration/
│   ├── Extensions/
│   ├── Interceptors/
│   ├── UnitOfWork/
│   ├── KukulcanDbContextBase.cs
│   └── TenantModelCacheKeyFactory.cs
├── SourceClient/
│   └── KUKULCAN.SharedKernel.Database.SourceClient/
├── Tests/
│   ├── KUKULCAN.SharedKernel.Database.Tests/
│   └── KUKULCAN.SharedKernel.Database.Integration/
└── Documentation/
```

The `SourceClient` project is a console client used to exercise the database infrastructure from a consuming application perspective. The two test projects deliberately separate deterministic unit tests from provider-backed integration tests.

## Core Components

### `KukulcanDbContextBase`

Provides common provider configuration, model-configuration discovery, global filters, tenant-aware model caching and persistence interceptor registration for derived module contexts.

### Persistence Interceptors

| Component | Responsibility |
|---|---|
| `AuditSaveChangesInterceptor` | Sets `CreatedOn` for added auditable entities and `ModifiedOn` for modified entities. |
| `SoftDeleteInterceptor` | Converts deletes of `ISoftDelete` entities into logical deletes and records `DeletedOn`. |
| `DomainEventDispatchInterceptor` | Dispatches pending domain events after a successful save and clears them. |
| `ImmutableEntityInterceptor` | Rejects updates and deletes of `IImmutable` entities. |
| `SlowQueryInterceptor` | Logs commands exceeding the configured slow-query threshold. |

### Unit of Work

`IUnitOfWork` exposes asynchronous saving and explicit transaction lifecycle operations. Repository abstractions are intentionally outside this package.

## Tenant Isolation and Model Caching

`ITenantContext` supplies the current `Guid` tenant identifier. `ApplyTenantFilter` applies a global filter to entity types that expose a `Guid TenantId` property.

`TenantModelCacheKeyFactory` includes the current tenant identifier and EF Core design-time state in the model cache key for `KukulcanDbContextBase` contexts. This prevents tenant-specific EF Core models from being incorrectly shared while preserving normal model-cache reuse for the same tenant and design-time state.

Tenant awareness remains a persistence concern and is not added to SharedKernel merely for EF Core support.

## Providers

The production database package does not reference concrete provider packages. Consumers add the provider they need, such as:

- `Microsoft.EntityFrameworkCore.SqlServer`
- `Npgsql.EntityFrameworkCore.PostgreSQL`

The current `DatabaseProvider` enum supports `SqlServer` and `PostgresSql`. Provider configuration is resolved dynamically in `KukulcanDbContextBase` so the production package remains provider-neutral at package level.

## Configuration

Options are bound from `Kukulcan:Database`. The configuration model includes provider selection, connection string, command timeout, retry policy, pool options, migration/seed options and EF Core diagnostic flags. Sensitive-data logging is disabled by default and should remain disabled in production.

## Registration

```csharp
services.AddKukulcanDbContext<MyModuleDbContext>(configuration);
```

The registration helper binds `KukulcanDatabaseOptions`, validates the required connection string, registers the derived context, registers `IUnitOfWork` as a scoped service and registers `SlowQueryInterceptor` as a singleton.

## Requirements

- .NET 10
- `KUKULCAN.SharedKernel` 1.0.0 or compatible
- EF Core 10
- Microsoft.Extensions Options, DI, Logging and Configuration abstractions 10
- A concrete EF Core provider package supplied by the consuming project when required

## Quality and Test Coverage

Nullable reference types are enabled, warnings are treated as errors and XML documentation generation is enabled. Public APIs are documented and persistence behavior is covered by behavior-focused tests.

The **current unit-test coverage baseline** for the production assembly is **100% line coverage (221/221 lines)** and **97.36% branch coverage (74/76 branches)**. **PostgreSQL is the reference database management system (DBMS) used by the integration test suite** for persistence-level validation; PostgreSQL is not the reason the unit-test line and branch percentages are 100% and 97.36% respectively.

The two test layers have different responsibilities:

- `KUKULCAN.SharedKernel.Database.Tests` provides deterministic unit coverage of guard clauses, provider-selection logic, reflection/configuration paths, unit-of-work contracts and synchronous/asynchronous interceptor behavior.
- `KUKULCAN.SharedKernel.Database.Integration` validates persistence behavior against a real PostgreSQL database, including connectivity, tenant isolation, model-cache isolation, interception and transaction behavior.

The integration suite is not the accepted coverage threshold. Its purpose is functional verification against the PostgreSQL DBMS, while the unit-test report defines the deterministic code-path and branch coverage baseline.

The two uncovered unit-test branches are intentional defensive branches in `KukulcanDbContextBase.ConfigureSqlServer` and `KukulcanDbContextBase.ConfigurePostgresSql`. They are the failure sides of the null-coalescing provider type-resolution expressions used when a required EF Core provider assembly cannot be resolved:

```csharp
Type.GetType("...Microsoft.EntityFrameworkCore.SqlServer")
    ?? throw NotInstalled("Microsoft.EntityFrameworkCore.SqlServer");

Type.GetType("...Npgsql.EntityFrameworkCore.PostgreSQL")
    ?? throw NotInstalled("Npgsql.EntityFrameworkCore.PostgreSQL");
```

The unit-test project references both provider packages, so the supported test environment contains the assemblies and those defensive `null` branches cannot be reached naturally. Forcing assembly absence solely to obtain a numerical 100% branch-coverage result would require an artificial runtime condition and would reduce the representativeness of the test suite.

The resulting **97.36% branch coverage is therefore an intentional and reviewed boundary, not an unsupported production behavior left untested**.

## Integration Testing

Integration tests are maintained separately in `Tests/KUKULCAN.SharedKernel.Database.Integration`.

The integration test database is **PostgreSQL**. The suite uses a real PostgreSQL instance to validate provider connectivity, persistence, tenant isolation, model-cache isolation, soft-delete interception, audit timestamps, domain-event dispatch, immutable-entity enforcement, slow-query diagnostics and database transactions through `UnitOfWork<TContext>`.

The integration project references `Npgsql.EntityFrameworkCore.PostgreSQL`, `Testcontainers.PostgreSql` and `coverlet.collector`. Coverage collection is available for integration runs, but **integration coverage is not used as the project's acceptance threshold**.

GitHub Actions provisions PostgreSQL 16 as a service container for the dedicated integration workflow. Local execution can use the configured PostgreSQL integration connection string or override it through `KUKULCAN_DATABASE_INTEGRATION_CONNECTION_STRING`.

## License

See the repository `LICENSE` file for the applicable GPL terms.
