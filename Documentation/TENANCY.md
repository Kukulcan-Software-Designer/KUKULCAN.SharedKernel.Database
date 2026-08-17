# Tenant Isolation

## Purpose

Tenant isolation is implemented at the persistence boundary.

## Contract

`ITenantContext` exposes:

``` csharp
Guid TenantId { get; }
```

The current tenant is supplied to the persistence infrastructure.

## Query Filtering

Entities exposing a `TenantId` property receive a global query filter.

``` mermaid
flowchart TD
    C["ITenantContext"] --> T["Current TenantId"]
    T --> F["EF Core global query filter"]
    F --> Q["Entity query"]
    Q --> R["Only current-tenant rows"]
```

## Why Is Tenancy Not in SharedKernel?

Tenant awareness is not a universal domain primitive for every KUKULCAN
bounded context.

Keeping it here:

-   avoids coupling SharedKernel to multi-tenancy;
-   keeps persistence isolation close to database access;
-   allows infrastructure to enforce tenant boundaries centrally.

## Security Consideration

Tenant filtering is a defense-in-depth mechanism. Applications must
still authenticate and authorize callers correctly.

The database filter should not be treated as the only security boundary.
