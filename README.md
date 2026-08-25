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

The latest successful `Code Coverage` workflow on `main` reports **132/132 passing unit tests** and the following Cobertura metrics for the production assembly:

| Metric | Result |
|---|---:|
| Line coverage | **96.68% (292/302)** |
| Branch coverage | **92.70% (89/96)** |

`KukulcanDbContextBase` is at **95.42% line coverage / 91.30% branch coverage**, while `SlowQueryInterceptor` is at **92.00% line coverage / 100% branch coverage**. `DomainEventDispatchInterceptor` and `KukulcanDatabaseStartupInitializer<TContext>` both have 100% line coverage but 50% branch coverage. The remaining production classes are at 100% line and branch coverage.

The uncovered production lines are concentrated in defensive provider-resolution/configuration paths of `KukulcanDbContextBase` and two lines in `SlowQueryInterceptor`. These figures are intentionally reported as measured rather than inflated through artificial test conditions.

Provider-specific integration tests complement unit coverage by validating the infrastructure against real Microsoft SQL Server, PostgreSQL and MySQL engines. Integration execution coverage is kept separate from the deterministic unit percentage.

See [`Documentation/COVERAGE.md`](Documentation/COVERAGE.md) for the complete coverage breakdown and current coverage policy.

## Integration Testing

The provider-specific integration projects use Testcontainers and real database engines. They validate persistence behavior including provider selection, tenant isolation, tenant-aware model caching, audit and soft-delete interception, domain-event dispatch, immutable-entity enforcement, slow-query diagnostics, cancellation, retry, migration/seed and transaction behavior.

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
