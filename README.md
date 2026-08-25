# KUKULCAN.SharedKernel.Database

## Overview

`KUKULCAN.SharedKernel.Database` is the shared persistence and Entity Framework Core infrastructure module used by KUKULCAN applications and bounded contexts. It builds on `KUKULCAN.SharedKernel` and centralizes database cross-cutting concerns without introducing business-domain rules into the shared domain model.

The library targets **.NET 10** and **EF Core 10**. Concrete provider packages are deliberately supplied by consuming infrastructure or host projects rather than by the production database library itself.

## Responsibilities

- `KukulcanDbContextBase` for common EF Core configuration.
- SQL Server, PostgreSQL and MySQL provider selection through `DatabaseProvider`.
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
│   ├── KUKULCAN.SharedKernel.Database.PostgreSQL.Integration/
│   ├── KUKULCAN.SharedKernel.Database.SQLServer.Integration/
│   └── KUKULCAN.SharedKernel.Database.MySQL.Integration/
└── Documentation/
```

The `SourceClient` project is a console client used to exercise the database infrastructure from a consuming application perspective. The test projects deliberately separate deterministic unit tests from provider-specific integration tests.

## Providers

The production database package remains provider-neutral at package level. Consumers supply the concrete provider packages they require:

- `Microsoft.EntityFrameworkCore.SqlServer`
- `Npgsql.EntityFrameworkCore.PostgreSQL`
- `MySql.EntityFrameworkCore`

The current provider configuration supports Microsoft SQL Server, PostgreSQL and MySQL. Provider configuration is resolved dynamically in `KukulcanDbContextBase` so the production package does not need to publish a concrete database provider dependency.

## Quality and Test Coverage

Nullable reference types are enabled, warnings are treated as errors and XML documentation generation is enabled. Public APIs are documented and persistence behavior is covered by behavior-focused tests.

The latest successful unit coverage report on `main` reports:

| Metric | Result |
|---|---:|
| Line coverage | **99.13% (228/230)** |
| Branch coverage | **100% (74/74)** |

`KukulcanDbContextBase` is the only production class below 100% line coverage at **98.26%**. Every other production class in the report has 100% line and branch coverage. The remaining two lines are defensive provider-resolution code; the logical branches are already fully covered. The suite is not weakened or altered merely to manufacture a 100% line metric through artificial assembly-loading conditions.

Provider-specific integration coverage is measured separately against real database engines. The integration workflow is configured to generate one Cobertura report per DBMS for Microsoft SQL Server, PostgreSQL and MySQL.

See [`Documentation/COVERAGE.md`](Documentation/COVERAGE.md) for the authoritative coverage report and provider-specific integration coverage results.

## Integration Testing

The provider-specific integration projects use Testcontainers and real database engines. They validate persistence behavior including provider selection, tenant isolation, tenant-aware model caching, audit and soft-delete interception, domain-event dispatch, immutable-entity enforcement, slow-query diagnostics, cancellation and transaction behavior.

Integration coverage is not used as a substitute for unit coverage. Unit tests measure deterministic production code paths and branches; integration tests measure provider-backed execution against real DBMS engines.

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

## License

See the repository `LICENSE` file for the applicable GPL terms.
