# KUKULCAN.SharedKernel.Database

## Overview

`KUKULCAN.SharedKernel.Database` is the shared persistence and Entity Framework Core infrastructure module used by KUKULCAN applications and bounded contexts. It builds on `KUKULCAN.SharedKernel` and centralizes database cross-cutting concerns without introducing business-domain rules into the shared domain model.

The library targets **.NET 10** and **EF Core 10**. Provider packages are deliberately supplied by consuming applications or infrastructure projects.

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
│   └── KukulcanDbContextBase.cs
└── SourceClient/
```

## Core Components

### `KukulcanDbContextBase`

Provides common provider configuration, model-configuration discovery, global filters and persistence interceptors for derived module contexts.

### Persistence Interceptors

| Component | Responsibility |
|---|---|
| `AuditSaveChangesInterceptor` | Sets `CreatedOn` for added auditable entities and `ModifiedOn` for modified entities. |
| `SoftDeleteInterceptor` | Converts deletes of `ISoftDelete` entities into logical deletes and records `DeletedOn`. |
| `DomainEventDispatchInterceptor` | Dispatches pending domain events after a successful save and clears them. |
| `ImmutableEntityInterceptor` | Rejects updates and deletes of `IImmutable` entities. |
| `SlowQueryInterceptor` | Logs commands exceeding `SlowQueryThresholdMs`. |

### Unit of Work

`IUnitOfWork` exposes asynchronous saving and explicit transaction lifecycle operations. Repository abstractions are intentionally outside this package.

## Tenant Isolation

`ITenantContext` supplies the current `Guid` tenant identifier. `ApplyTenantFilter` applies a global filter to entity types that expose a `Guid TenantId` property. Tenant awareness remains a persistence concern and is not added to SharedKernel merely for EF Core support.

## Providers

The core package does not reference provider packages. Consumers add the provider they need, such as `Microsoft.EntityFrameworkCore.SqlServer` or `Npgsql.EntityFrameworkCore.PostgreSQL`. The current `DatabaseProvider` enum supports `SqlServer` and `PostgresSql`.

## Configuration

Options are bound from `Kukulcan:Database`. Sensitive-data logging is disabled by default and should remain disabled in production.

## Registration

```csharp
services.AddKukulcanDbContext<MyModuleDbContext>(configuration);
```

The registration helper binds options, registers the context, registers `IUnitOfWork` and registers the slow-query interceptor.

## Requirements

- .NET 10
- `KUKULCAN.SharedKernel` 1.0.0 or compatible
- EF Core 10
- Microsoft.Extensions Options, DI, Logging and Configuration abstractions 10
- A database provider package supplied by the consuming project when required

## Quality and Test Coverage

Nullable reference types are enabled, warnings are treated as errors and XML documentation generation is enabled. Public APIs are documented and persistence behavior is covered by behavior-focused tests.

The current test suite achieves **100% line coverage (221/221 lines)** and **97.36% branch coverage (74/76 branches)** for the database library.

The two uncovered branches are intentional defensive branches in `KukulcanDbContextBase.ConfigureSqlServer` and `KukulcanDbContextBase.ConfigurePostgresSql`. They belong to the null-coalescing type-resolution expressions that handle the case where the corresponding EF Core provider assembly is not installed:

```csharp
Type.GetType("...Microsoft.EntityFrameworkCore.SqlServer")
    ?? throw NotInstalled("Microsoft.EntityFrameworkCore.SqlServer");

Type.GetType("...Npgsql.EntityFrameworkCore.PostgreSQL")
    ?? throw NotInstalled("Npgsql.EntityFrameworkCore.PostgreSQL");
```

The test project intentionally references both provider packages in order to exercise the real SQL Server and PostgreSQL configuration paths. Consequently, those assemblies are available during test execution and the runtime type resolution succeeds. Forcing the assemblies to appear unavailable would require manipulating assembly loading or introducing production-only test seams solely to satisfy a coverage metric. That would make the tests less deterministic and less representative of the supported runtime configuration.

The resulting **97.36% branch coverage is therefore intentional and documented**, rather than an indication of an untested supported behavior. The `NotInstalled` behavior itself is tested directly, including the missing-inner-exception scenario.

## License

See the repository `LICENSE` file for the applicable GPL terms.
