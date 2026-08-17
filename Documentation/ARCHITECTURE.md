# Architecture

## Architectural Style

`KUKULCAN.SharedKernel.Database` is an infrastructure-oriented library
that sits between the domain-oriented SharedKernel and concrete
application persistence.

It follows a **Dependency Inversion** approach:

-   domain contracts live in SharedKernel;
-   persistence implementations live here;
-   concrete provider packages belong to consuming Infrastructure/Host
    projects.

## Boundary

``` mermaid
flowchart LR
    SK["SharedKernel\nDomain contracts"]
    DB["SharedKernel.Database\nPersistence infrastructure"]
    APP["Bounded Context"]
    EF["EF Core"]
    SQL["Concrete provider"]

    SK --> DB
    DB --> EF
    APP --> DB
    APP --> SQL
```

## Why Not Put Database Infrastructure in SharedKernel?

SharedKernel is intended to provide reusable domain-oriented primitives
and contracts.

EF Core introduces:

-   ORM-specific types;
-   change tracking;
-   database metadata;
-   migrations;
-   provider configuration;
-   connection behavior.

Those concepts are infrastructure concerns and would contaminate the
reusable domain foundation if placed there.

## Why a Base DbContext?

`KukulcanDbContextBase` centralizes concerns that are common to multiple
KUKULCAN modules.

Without the base context, each module would need to repeat:

-   interceptor registration;
-   global filters;
-   configuration discovery;
-   provider setup;
-   sensitive logging configuration;
-   detailed-error configuration.

The base class provides a single integration point.

## Why Provider Packages Are Not Referenced

Provider selection is deployment-specific.

A CRM deployment may use SQL Server while another deployment may use
PostgreSQL.

Keeping provider packages outside the library:

-   reduces transitive dependencies;
-   avoids forcing unused providers onto consumers;
-   keeps the library provider-neutral at package level;
-   allows each module/host to control its infrastructure.

## Assembly Configuration Discovery

`KukulcanDbContextBase` calls:

``` csharp
modelBuilder.ApplyConfigurationsFromAssembly(GetType().Assembly);
```

The derived context therefore owns its entity configurations.

This supports modular persistence while keeping configuration close to
the bounded context that owns the entities.

## Global Conventions

Global persistence rules are implemented through
`ModelBuilderExtensions`.

This is preferable to repeating query filters in every entity
configuration because:

-   the rule is centralized;
-   new entities automatically inherit the behavior;
-   omission from an individual mapping is less likely;
-   the convention expresses an infrastructure-wide policy.

## Interceptor Strategy

EF Core interceptors are used for behavior that surrounds persistence
operations.

``` mermaid
flowchart TD
    S["SaveChanges"] --> A["Audit"]
    A --> SD["Soft Delete"]
    SD --> IM["Immutable Enforcement"]
    IM --> P["EF Core persistence"]
    P --> DE["Domain Event Dispatch"]
```

The model is intentionally cross-cutting: application services do not
need to remember to execute these concerns manually.

## Architectural Trade-offs

### Advantages

-   Strong reuse.
-   Centralized persistence policy.
-   Low duplication.
-   Provider dependencies remain consumer-controlled.
-   Domain remains independent from EF Core.
-   Cross-cutting behavior is testable.

### Costs

-   Reflection is used for provider configuration.
-   Global filters require conventions to be understood by developers.
-   Interceptor ordering matters.
-   Some behavior is less explicit than calling a service directly.

These costs are accepted because the library's primary goal is
consistent infrastructure behavior across modules.
