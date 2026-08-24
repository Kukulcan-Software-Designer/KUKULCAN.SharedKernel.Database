# Architecture Decision Records

## ADR-001 --- Keep EF Core Outside SharedKernel

### Status

Accepted.

### Context

SharedKernel is reused by domain and application components. EF Core is an infrastructure technology.

### Decision

EF Core integration belongs to `KUKULCAN.SharedKernel.Database`.

### Rationale

This prevents ORM-specific dependencies from entering domain code and keeps the SharedKernel reusable.

### Consequences

- Domain independence.
- Cleaner dependency graph.
- Persistence behavior must be wired explicitly.

------------------------------------------------------------------------

## ADR-002 --- Use a Base DbContext

### Status

Accepted.

### Decision

Common database behavior is centralized in `KukulcanDbContextBase`.

### Rationale

Every bounded context should not have to reimplement the same infrastructure behavior.

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

Consumers have explicit control over provider dependencies, while this library remains smaller.

------------------------------------------------------------------------

## ADR-004 --- Implement Cross-Cutting Persistence Rules with Interceptors

### Status

Accepted.

### Decision

Auditing, soft delete, immutable enforcement and domain-event dispatch are implemented with EF Core interceptors.

### Rationale

These concerns surround persistence operations rather than representing individual domain behaviors.

### Consequences

Behavior is centralized and difficult to accidentally omit, but interceptor ordering must remain intentional.

------------------------------------------------------------------------

## ADR-005 --- Tenant Isolation Is a Persistence Concern

### Status

Accepted.

### Decision

Tenant filtering is represented by `ITenantContext` and EF Core query filters.

### Rationale

Tenant isolation at query time is infrastructure behavior. The database layer needs the current tenant to enforce row isolation.

### Consequences

SharedKernel remains independent from tenancy infrastructure while persistence automatically scopes queries.

------------------------------------------------------------------------

## ADR-006 --- Separate Integration Tests by Database Provider

### Status

Accepted.

### Context

The library supports multiple EF Core database providers with provider-specific behavior that should be validated against real database engines. Keeping all provider-backed tests in one project couples unrelated provider dependencies, fixtures and CI execution.

### Decision

Provider-backed integration tests are maintained in two dedicated projects:

- `KUKULCAN.SharedKernel.Database.PostgreSQL.Integration` for PostgreSQL.
- `KUKULCAN.SharedKernel.Database.SQLServer.Integration` for Microsoft SQL Server.

### Rationale

This keeps provider-specific dependencies and infrastructure isolated while making each integration suite independently executable locally and in CI. It also makes failures unambiguous by provider.

### Consequences

Positive:

- Provider dependencies are isolated.
- Provider fixtures remain self-contained.
- CI can execute provider suites independently.
- Local test execution is simpler and more targeted.
- Provider-specific diagnostics are easier to interpret.

Negative:

- Some integration support code may be duplicated between provider projects.
- The solution contains two integration projects instead of one.

The duplication is accepted because the two projects represent distinct provider contracts.
