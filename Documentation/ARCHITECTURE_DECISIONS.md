# Architecture Decision Records

## ADR-001 --- Keep EF Core Outside SharedKernel

### Status

Accepted.

### Context

SharedKernel is reused by domain and application components. EF Core is
an infrastructure technology.

### Decision

EF Core integration belongs to `KUKULCAN.SharedKernel.Database`.

### Rationale

This prevents ORM-specific dependencies from entering domain code and
keeps the SharedKernel reusable.

### Consequences

Positive:

-   domain independence;
-   cleaner dependency graph;
-   easier replacement or evolution of persistence infrastructure.

Negative:

-   additional infrastructure layer;
-   persistence behavior must be wired explicitly.

------------------------------------------------------------------------

## ADR-002 --- Use a Base DbContext

### Status

Accepted.

### Decision

Common database behavior is centralized in `KukulcanDbContextBase`.

### Rationale

Every bounded context should not have to reimplement the same
infrastructure behavior.

### Consequences

Modules gain a standard persistence integration point.

------------------------------------------------------------------------

## ADR-003 --- Keep Concrete Providers Out of the Package

### Status

Accepted.

### Decision

Concrete provider packages are installed by consuming modules/hosts.

### Rationale

Provider choice is deployment-specific.

### Consequences

Consumers have explicit control over provider dependencies, while this
library remains smaller.

------------------------------------------------------------------------

## ADR-004 --- Implement Cross-Cutting Persistence Rules with Interceptors

### Status

Accepted.

### Decision

Auditing, soft delete, immutable enforcement and domain-event dispatch
are implemented with EF Core interceptors.

### Rationale

These concerns surround persistence operations rather than representing
individual domain behaviors.

### Consequences

Behavior is centralized and difficult to accidentally omit, but
interceptor ordering must remain intentional.

------------------------------------------------------------------------

## ADR-005 --- Tenant Isolation Is a Persistence Concern

### Status

Accepted.

### Decision

Tenant filtering is represented by `ITenantContext` and EF Core query
filters.

### Rationale

Tenant isolation at query time is infrastructure behavior. The database
layer needs the current tenant to enforce row isolation.

### Consequences

SharedKernel remains independent from tenancy infrastructure while
persistence automatically scopes queries.
