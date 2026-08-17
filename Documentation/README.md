# KUKULCAN.SharedKernel.Database

## Purpose

`KUKULCAN.SharedKernel.Database` is the persistence and Entity Framework
Core infrastructure layer built on top of `KUKULCAN.SharedKernel`.

Its purpose is to centralize database concerns that are cross-cutting
across KUKULCAN modules without moving database-specific concerns into
the domain-oriented SharedKernel.

The library targets **.NET 10** and uses **Entity Framework Core 10**.

## Architectural Position

The dependency direction is intentionally one-way:

``` mermaid
flowchart TD
    SK["KUKULCAN.SharedKernel"]
    DB["KUKULCAN.SharedKernel.Database"]
    MOD["Module Infrastructure"]
    HOST["Application / Host"]
    EF["EF Core"]
    PROVIDER["Database Provider"]

    SK --> DB
    DB --> EF
    MOD --> DB
    MOD --> PROVIDER
    HOST --> MOD
```

`KUKULCAN.SharedKernel.Database` depends on `KUKULCAN.SharedKernel`, but
the SharedKernel does not depend on the database layer.

This prevents persistence infrastructure from leaking into domain code
and avoids circular dependencies.

## Why This Architecture

The project follows a **Shared Kernel + Infrastructure Boundary**
approach rather than placing Entity Framework Core directly inside the
domain layer.

The main reasons are:

1.  **Domain independence** --- domain entities and business rules do
    not require EF Core.
2.  **Infrastructure centralization** --- auditing, soft delete, tenancy
    filters, immutable persistence rules and domain-event dispatch are
    implemented once.
3.  **Provider independence** --- SQL Server and PostgreSQL provider
    packages are deliberately not referenced by this library.
4.  **Module reuse** --- individual bounded contexts can derive their
    DbContext from `KukulcanDbContextBase`.
5.  **Testability** --- cross-cutting persistence behavior can be tested
    independently from individual modules.
6.  **Dependency control** --- database providers remain dependencies of
    the consuming Infrastructure/Host project.

## Main Components

  -----------------------------------------------------------------------
  Component                           Responsibility
  ----------------------------------- -----------------------------------
  `KukulcanDbContextBase`             Common DbContext infrastructure

  `UnitOfWork<TContext>`              Transaction and persistence
                                      coordination

  `AuditSaveChangesInterceptor`       Populates audit timestamps

  `SoftDeleteInterceptor`             Converts physical deletes into
                                      logical deletes

  `ImmutableEntityInterceptor`        Prevents modification/deletion of
                                      immutable entities

  `DomainEventDispatchInterceptor`    Dispatches pending domain events
                                      after successful persistence

  `SlowQueryInterceptor`              Logs commands exceeding the
                                      configured threshold

  `ModelBuilderExtensions`            Applies global EF Core model
                                      conventions

  `ServiceCollectionExtensions`       Registers database infrastructure
                                      with dependency injection

  `KukulcanDatabaseOptions`           Centralized database configuration

  `DatabaseProvider`                  Supported relational provider
                                      selection

  `ITenantContext`                    Current tenant abstraction

  `IUnitOfWork`                       Persistence transaction abstraction

  `IImmutable`                        Append-only persistence marker
  -----------------------------------------------------------------------

## Cross-Cutting Persistence Pipeline

``` mermaid
flowchart TD
    A["Application changes aggregate/entity"] --> B["DbContext.SaveChanges"]
    B --> C["AuditSaveChangesInterceptor"]
    C --> D["SoftDeleteInterceptor"]
    D --> E["ImmutableEntityInterceptor"]
    E --> F["EF Core persistence"]
    F --> G["Successful save"]
    G --> H["DomainEventDispatchInterceptor"]
    H --> I["IDomainEventDispatcher"]
```

The exact interceptor ordering is controlled by registration in
`KukulcanDbContextBase`.

## Database Providers

The library references EF Core abstractions and relational
infrastructure, but deliberately does not reference a concrete provider.

Consumers install the provider they actually use, such as:

-   `Microsoft.EntityFrameworkCore.SqlServer`
-   `Npgsql.EntityFrameworkCore.PostgreSQL`
-   another supported provider when the consuming module implements the
    necessary configuration.

The current base implementation explicitly configures SQL Server and
PostgreSQL.

## Configuration

Configuration is bound from:

``` text
Kukulcan:Database
```

Example:

``` json
{
  "Kukulcan": {
    "Database": {
      "Provider": "PostgresSql",
      "ConnectionString": "Host=localhost;Database=Kukulcan;Username=Kukulcan;Password=...",
      "CommandTimeoutSeconds": 30,
      "EnableSensitiveDataLogging": false,
      "EnableDetailedErrors": false
    }
  }
}
```

Sensitive-data logging must remain disabled in production.

## Global Model Conventions

`ModelBuilderExtensions` applies two persistence-level conventions:

### Soft Delete

Entities implementing `ISoftDelete` receive a global query filter that
excludes logically deleted records.

``` mermaid
flowchart LR
    Q["EF Core query"] --> F["Global soft-delete filter"]
    F --> R["Only IsDeleted = false"]
```

### Tenant Isolation

Entities exposing a `TenantId` property receive a global tenant filter
based on `ITenantContext`.

``` mermaid
flowchart LR
    T["ITenantContext.TenantId"] --> F["Global tenant filter"]
    F --> Q["EF Core query"]
    Q --> D["Tenant-scoped rows"]
```

Tenant awareness remains a persistence concern and is intentionally not
introduced into SharedKernel.

## Unit of Work

`UnitOfWork<TContext>` wraps a concrete `KukulcanDbContextBase` and
exposes:

-   `SaveChangesAsync`
-   `BeginTransactionAsync`
-   `CommitTransactionAsync`
-   `RollbackTransactionAsync`
-   `EndTransactionAsync`

Transactions are explicitly controlled by the caller.

## Immutability

`IImmutable` marks entities as append-only from the persistence
perspective.

After insertion, the interceptor rejects:

-   updates;
-   deletes.

This is implemented at the persistence boundary so the rule is enforced
even when changes originate outside a particular application service.

## Domain Events

The database layer does not implement a messaging bus.

Instead, `DomainEventDispatchInterceptor` depends on the SharedKernel
`IDomainEventDispatcher` abstraction.

``` mermaid
flowchart LR
    A["Aggregate"] --> E["Pending IDomainEvent"]
    E --> S["Successful SaveChanges"]
    S --> I["DomainEventDispatchInterceptor"]
    I --> D["IDomainEventDispatcher"]
```

This keeps the persistence layer independent of the concrete dispatching
technology.

## Slow Query Diagnostics

`SlowQueryInterceptor` monitors EF Core database commands.

The default threshold is:

``` text
500 ms
```

Commands exceeding the threshold generate a warning.

Sensitive SQL text is only included when:

``` text
EnableSensitiveDataLogging = true
```

Production deployments should keep this option disabled.

## Dependency Rules

The intended dependency model is:

``` mermaid
flowchart BT
    Domain["Domain / SharedKernel"] -->|contracts| Database["SharedKernel.Database"]
    Database --> EF["EF Core"]
    Module["Bounded Context Infrastructure"] --> Database
    Module --> Provider["Concrete EF Provider"]
```

The following rule is intentionally avoided:

``` text
SharedKernel -> EF Core
```

because it would couple the domain-oriented foundation to a specific
persistence technology.

## Package Design

The package contains:

-   SharedKernel reference;
-   EF Core core and relational packages;
-   Microsoft Extensions abstractions required for configuration, DI and
    logging.

Concrete database providers remain outside this package.

## Documentation

Additional architectural documentation is available in:

-   `docs/ARCHITECTURE.md`
-   `docs/ARCHITECTURE_DECISIONS.md`
-   `docs/PERSISTENCE_PIPELINE.md`
-   `docs/TENANCY.md`
-   `docs/SOFT_DELETE.md`
-   `docs/AUDITING.md`
-   `docs/DOMAIN_EVENTS.md`
-   `docs/IMMUTABILITY.md`
-   `docs/UNIT_OF_WORK.md`
-   `docs/CONFIGURATION.md`
-   `docs/TESTING.md`
