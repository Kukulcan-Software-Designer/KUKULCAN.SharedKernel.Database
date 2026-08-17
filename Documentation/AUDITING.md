# Auditing

## Purpose

The auditing mechanism automatically maintains persistence timestamps
defined by the SharedKernel `IAuditable` contract.

## Behavior

``` mermaid
flowchart LR
    A["Entity implements IAuditable"] --> B{"Entity state"}
    B -->|Added| C["CreatedOn = clock.UtcNow"]
    B -->|Modified| D["ModifiedOn = clock.UtcNow"]
```

## Why an Interceptor?

Auditing is a persistence concern.

Putting timestamp management into every application service would create
duplication and allow some write paths to forget auditing.

The interceptor ensures that normal EF Core persistence paths receive
consistent behavior.

## Clock Abstraction

`AuditSaveChangesInterceptor` depends on `IClock`.

This is preferable to calling:

``` csharp
DateTimeOffset.UtcNow
```

directly because tests can provide a deterministic clock.

## Scope

The current implementation manages:

-   `CreatedOn`;
-   `ModifiedOn`.

It does not introduce user identity auditing such as `CreatedBy` or
`ModifiedBy`.
